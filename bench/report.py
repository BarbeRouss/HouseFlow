#!/usr/bin/env python3
"""Génère docs/perf/rust-vs-dotnet.md à partir des résultats de run-backend.sh.

Stdlib uniquement. Les deux côtés sont optionnels : un backend absent donne des
colonnes « n/a » au lieu d'un plantage (utile tant que le port Rust n'existe pas).

Usage :
    python3 bench/report.py --dotnet <dir> --rust <dir> [--out docs/perf/rust-vs-dotnet.md]
                            [--raw-dir docs/perf/raw] [--no-copy-raw]
"""

from __future__ import annotations

import argparse
import json
import math
import os
import platform
import re
import shutil
import subprocess
import sys
from datetime import datetime, timezone
from pathlib import Path

# Ordre d'affichage figé : il structure le rapport, du plancher framework aux
# chemins d'écriture.
SCENARIO_ORDER = [
    "health",
    "login",
    "houses_list",
    "house_detail",
    "devices_list",
    "device_detail",
    "upcoming_tasks",
    "maintenance_history",
    "create_instance",
    "create_device",
]

SCENARIO_LABELS = {
    "health": "Sonde `GET /health` (plancher framework)",
    "login": "`POST /api/v1/auth/login` (bcrypt cost 11 + écritures)",
    "houses_list": "`GET /api/v1/houses`",
    "house_detail": "`GET /api/v1/houses/{id}` (avec scores)",
    "devices_list": "`GET /api/v1/houses/{id}/devices`",
    "device_detail": "`GET /api/v1/devices/{id}`",
    "upcoming_tasks": "`GET /api/v1/upcoming-tasks`",
    "maintenance_history": "`GET /api/v1/devices/{id}/maintenance-history`",
    "create_instance": "`POST /api/v1/maintenance-types/{id}/instances` (écriture + audit)",
    "create_device": "`POST /api/v1/houses/{id}/devices` (écriture)",
}

BACKENDS = ["dotnet", "rust"]
BACKEND_LABELS = {"dotnet": ".NET", "rust": "Rust"}


# --------------------------------------------------------------------------- #
# Lecture des résultats
# --------------------------------------------------------------------------- #
def load_run(directory: str | None) -> dict:
    """Charge un répertoire de résultats. Renvoie {} s'il est absent ou vide."""
    if not directory:
        return {}
    path = Path(directory)
    if not path.is_dir():
        return {}

    scenarios: dict[str, dict] = {}
    for file in sorted(path.glob("scenario-*.json")):
        try:
            with file.open(encoding="utf-8") as handle:
                data = json.load(handle)
        except (OSError, json.JSONDecodeError) as exc:
            print(f"report: {file} illisible ({exc})", file=sys.stderr)
            continue
        name = data.get("name") or file.stem.removeprefix("scenario-")
        data.pop("raw", None)  # le brut reste dans raw/, pas en mémoire
        scenarios[name] = data

    process = {}
    process_file = path / "process.json"
    if process_file.is_file():
        try:
            with process_file.open(encoding="utf-8") as handle:
                process = json.load(handle)
        except (OSError, json.JSONDecodeError) as exc:
            print(f"report: {process_file} illisible ({exc})", file=sys.stderr)

    seed = {}
    seed_file = path / "seed.json"
    if seed_file.is_file():
        try:
            with seed_file.open(encoding="utf-8") as handle:
                seed = json.load(handle)
            seed.pop("token", None)  # pas de JWT dans un fichier commité
            seed.pop("password", None)
        except (OSError, json.JSONDecodeError):
            seed = {}

    if not scenarios and not process:
        return {}
    return {"dir": str(path), "scenarios": scenarios, "process": process, "seed": seed}


# --------------------------------------------------------------------------- #
# Environnement
# --------------------------------------------------------------------------- #
def run_cmd(args: list[str]) -> str | None:
    try:
        out = subprocess.run(args, capture_output=True, text=True, timeout=30, check=False)
    except (OSError, subprocess.SubprocessError):
        return None
    if out.returncode != 0:
        return None
    return out.stdout.strip() or None


def cpu_model() -> str:
    try:
        with open("/proc/cpuinfo", encoding="utf-8") as handle:
            for line in handle:
                if line.startswith("model name"):
                    return line.split(":", 1)[1].strip()
    except OSError:
        pass
    return platform.processor() or "inconnu"


def total_ram_gb() -> str:
    try:
        with open("/proc/meminfo", encoding="utf-8") as handle:
            for line in handle:
                if line.startswith("MemTotal:"):
                    kb = int(line.split()[1])
                    return f"{kb / 1024 / 1024:.1f} Go"
    except (OSError, ValueError):
        pass
    return "inconnu"


def os_name() -> str:
    try:
        with open("/etc/os-release", encoding="utf-8") as handle:
            for line in handle:
                if line.startswith("PRETTY_NAME="):
                    return line.split("=", 1)[1].strip().strip('"')
    except OSError:
        pass
    return platform.platform()


def dotnet_version() -> str:
    for candidate in ("dotnet", "/usr/share/dotnet/dotnet"):
        version = run_cmd([candidate, "--version"])
        if version:
            return version
    return "n/a"


def rustc_version() -> str:
    for candidate in ("rustc", os.path.expanduser("~/.cargo/bin/rustc")):
        version = run_cmd([candidate, "--version"])
        if version:
            return version.replace("rustc ", "")
    return "n/a"


def git_sha(root: Path) -> str:
    sha = run_cmd(["git", "-C", str(root), "rev-parse", "--short", "HEAD"])
    return sha or "n/a"


def environment(root: Path, runs: dict) -> dict:
    postgres = "n/a"
    for backend in BACKENDS:
        version = (runs.get(backend) or {}).get("process", {}).get("postgres_version")
        if version:
            postgres = version
            break
    return {
        "CPU": f"{cpu_model()} ({os.cpu_count()} cœurs logiques)",
        "RAM": total_ram_gb(),
        "OS": os_name(),
        "Noyau": platform.release(),
        ".NET SDK": dotnet_version(),
        "rustc": rustc_version(),
        "PostgreSQL": postgres,
        "Date": datetime.now(timezone.utc).strftime("%Y-%m-%d %H:%M UTC"),
        "Commit": git_sha(root),
    }


# --------------------------------------------------------------------------- #
# Formatage
# --------------------------------------------------------------------------- #
def fmt_num(value, digits: int = 0, suffix: str = "") -> str:
    if value is None:
        return "n/a"
    try:
        number = float(value)
    except (TypeError, ValueError):
        return "n/a"
    if math.isnan(number) or math.isinf(number):
        return "n/a"
    if digits == 0:
        text = f"{number:,.0f}".replace(",", " ")
    else:
        text = f"{number:,.{digits}f}".replace(",", " ")
    return text + suffix


def ratio(rust_value, dotnet_value) -> str:
    """Rapport Rust / .NET, formaté ×N. n/a si l'un des deux manque."""
    try:
        rust_number = float(rust_value)
        dotnet_number = float(dotnet_value)
    except (TypeError, ValueError):
        return "n/a"
    if dotnet_number <= 0 or rust_number <= 0:
        return "n/a"
    return f"×{rust_number / dotnet_number:.2f}"


def table(headers: list[str], rows: list[list[str]]) -> str:
    lines = ["| " + " | ".join(headers) + " |",
             "|" + "|".join("---" for _ in headers) + "|"]
    for row in rows:
        lines.append("| " + " | ".join(row) + " |")
    return "\n".join(lines)


def scenario_names(runs: dict) -> list[str]:
    seen = []
    for name in SCENARIO_ORDER:
        if any(name in (runs.get(b) or {}).get("scenarios", {}) for b in BACKENDS):
            seen.append(name)
    for backend in BACKENDS:
        for name in (runs.get(backend) or {}).get("scenarios", {}):
            if name not in seen:
                seen.append(name)
    return seen


def geometric_mean_ratio(runs: dict) -> float | None:
    """Moyenne géométrique du rapport de débit Rust/.NET sur les scénarios communs."""
    ratios = []
    for name in scenario_names(runs):
        dotnet = (runs.get("dotnet") or {}).get("scenarios", {}).get(name)
        rust = (runs.get("rust") or {}).get("scenarios", {}).get(name)
        if not dotnet or not rust:
            continue
        try:
            d_rps = float(dotnet.get("requests_per_sec") or 0)
            r_rps = float(rust.get("requests_per_sec") or 0)
        except (TypeError, ValueError):
            continue
        if d_rps > 0 and r_rps > 0:
            ratios.append(r_rps / d_rps)
    if not ratios:
        return None
    return math.exp(sum(math.log(r) for r in ratios) / len(ratios))


# --------------------------------------------------------------------------- #
# Sections du rapport
# --------------------------------------------------------------------------- #
def methodology_section(runs: dict) -> str:
    sample = None
    for backend in BACKENDS:
        scenarios = (runs.get(backend) or {}).get("scenarios", {})
        if scenarios:
            sample = next(iter(scenarios.values()))
            break
    duration = sample.get("duration") if sample else "n/a"
    concurrency = sample.get("concurrency") if sample else "n/a"
    tool = sample.get("tool") if sample else "n/a"

    login = None
    for backend in BACKENDS:
        login = (runs.get(backend) or {}).get("scenarios", {}).get("login") or login
    login_concurrency = login.get("concurrency") if login else "n/a"

    seed = {}
    for backend in BACKENDS:
        seed = (runs.get(backend) or {}).get("seed") or seed
        if seed:
            break
    counts = seed.get("counts", {})
    dataset = (
        f"{counts.get('houses', '?')} maisons, {counts.get('devices', '?')} appareils, "
        f"{counts.get('maintenanceTypes', '?')} types d'entretien, "
        f"{counts.get('maintenanceInstances', '?')} entretiens passés"
        if counts else "n/a"
    )

    databases = " / ".join(
        f"`{(runs.get(b) or {}).get('process', {}).get('database', 'n/a')}` ({BACKEND_LABELS[b]})"
        for b in BACKENDS if runs.get(b)
    ) or "n/a"

    return "\n".join([
        "## Méthodologie",
        "",
        "- **Builds** : les deux backends sont compilés en configuration *release*",
        "  (`dotnet publish -c Release`, `cargo build --release`), jamais en debug.",
        "- **Base de données** : même serveur PostgreSQL, même schéma, une base dédiée et vierge",
        f"  par backend — {databases} —, migrée au démarrage par le backend lui-même.",
        f"- **Jeu de données** : identique des deux côtés, créé via l'API publique ({dataset}).",
        "  Les entretiens sont datés dans le passé pour que scores, statuts et prochaines",
        "  échéances soient non triviaux.",
        f"- **Charge** : `{tool}`, {concurrency} connexions simultanées, {duration} de mesure par",
        f"  scénario, précédées d'un run de chauffe jeté. Le scénario `login` tourne à",
        f"  {login_concurrency} connexions : bcrypt (cost 11) sature le CPU bien avant le réseau.",
        "- **Jeton** : un JWT frais est obtenu avant chaque scénario (durée de vie 15 min).",
        "- **Ordre** : les scénarios de lecture passent avant les scénarios d'écriture, pour que",
        "  les lectures mesurent toutes le même volume de données.",
        "- **Isolation** : un seul backend tourne à la fois, sur sa propre base et son propre port.",
        "",
    ])


def scenario_tables(runs: dict) -> str:
    names = scenario_names(runs)
    if not names:
        return "## Scénarios\n\n_Aucun résultat de scénario._\n"

    def cell(backend: str, name: str, key: str, digits: int = 0):
        scenario = (runs.get(backend) or {}).get("scenarios", {}).get(name)
        if not scenario:
            return "n/a"
        return fmt_num(scenario.get(key), digits)

    def value(backend: str, name: str, key: str):
        scenario = (runs.get(backend) or {}).get("scenarios", {}).get(name)
        return scenario.get(key) if scenario else None

    throughput_rows = []
    latency_rows = []
    for name in names:
        label = f"`{name}`"
        throughput_rows.append([
            label,
            cell("dotnet", name, "requests_per_sec"),
            cell("rust", name, "requests_per_sec"),
            ratio(value("rust", name, "requests_per_sec"), value("dotnet", name, "requests_per_sec")),
            cell("dotnet", name, "errors"),
            cell("rust", name, "errors"),
        ])
        latency_rows.append([
            label,
            cell("dotnet", name, "p50_ms", 2),
            cell("rust", name, "p50_ms", 2),
            cell("dotnet", name, "p95_ms", 2),
            cell("rust", name, "p95_ms", 2),
            cell("dotnet", name, "p99_ms", 2),
            cell("rust", name, "p99_ms", 2),
        ])

    legend_rows = [[f"`{name}`", SCENARIO_LABELS.get(name, "—")] for name in names]

    return "\n".join([
        "## Scénarios",
        "",
        table(["Scénario", "Endpoint"], legend_rows),
        "",
        "### Débit et erreurs",
        "",
        table(
            ["Scénario", ".NET req/s", "Rust req/s", "Rapport", ".NET erreurs", "Rust erreurs"],
            throughput_rows,
        ),
        "",
        "Rapport = req/s Rust ÷ req/s .NET (supérieur à ×1 : Rust plus rapide).",
        "",
        "### Latences (ms)",
        "",
        table(
            ["Scénario", ".NET p50", "Rust p50", ".NET p95", "Rust p95", ".NET p99", "Rust p99"],
            latency_rows,
        ),
        "",
    ])


def process_table(runs: dict) -> str:
    def proc(backend: str, key: str, digits: int = 0, default=None):
        process = (runs.get(backend) or {}).get("process", {})
        if not process:
            return "n/a"
        value = process
        for part in key.split("."):
            if not isinstance(value, dict):
                return "n/a"
            value = value.get(part)
        if value is None:
            return "n/a" if default is None else default
        if isinstance(value, bool):
            return "oui" if value else "non"
        if isinstance(value, str):
            return value
        return fmt_num(value, digits)

    def mb(backend: str, key: str):
        process = (runs.get(backend) or {}).get("process", {})
        value = process.get(key) if process else None
        return fmt_num(value, 1) if value is not None else "n/a"

    def bytes_mb(backend: str, key: str):
        process = (runs.get(backend) or {}).get("process", {})
        value = (process.get("build") or {}).get(key) if process else None
        if value is None:
            return "n/a"
        return fmt_num(float(value) / 1048576, 1)

    def build_seconds(backend: str):
        process = (runs.get(backend) or {}).get("process", {})
        build = (process.get("build") or {}) if process else {}
        if not process:
            return "n/a"
        if build.get("skipped"):
            return "artefacts réutilisés"
        return fmt_num(build.get("seconds"), 1)

    rows = [
        ["Démarrage → premier 200 (ms)", proc("dotnet", "startup_ms"), proc("rust", "startup_ms")],
        ["RSS avant le seed (Mo)", mb("dotnet", "rss_mb_before_seed"), mb("rust", "rss_mb_before_seed")],
        ["RSS après le seed (Mo)", mb("dotnet", "rss_mb_after_seed"), mb("rust", "rss_mb_after_seed")],
        ["RSS après les scénarios (Mo)", mb("dotnet", "rss_mb_after_scenarios"), mb("rust", "rss_mb_after_scenarios")],
        ["Pic RSS observé (Mo)", mb("dotnet", "peak_rss_mb"), mb("rust", "peak_rss_mb")],
        ["CPU consommé sur la campagne (s)", proc("dotnet", "cpu_seconds_run", 1), proc("rust", "cpu_seconds_run", 1)],
        ["Artefact principal (Mo)", bytes_mb("dotnet", "artifact_bytes"), bytes_mb("rust", "artifact_bytes")],
        ["Livrable complet (Mo)", bytes_mb("dotnet", "deploy_bytes"), bytes_mb("rust", "deploy_bytes")],
        ["Temps de build (s)", build_seconds("dotnet"), build_seconds("rust")],
        ["Build à froid", proc("dotnet", "build.cold"), proc("rust", "build.cold")],
        ["Runtime", proc("dotnet", "runtime"), proc("rust", "runtime")],
    ]
    return "\n".join([
        "## Métriques process",
        "",
        table(["Métrique", ".NET", "Rust"], rows),
        "",
        "Le RSS est relevé dans `/proc/<pid>/status` (`VmRSS`, pic `VmHWM`), le CPU dans",
        "`/proc/<pid>/stat` (`utime + stime`) entre le début du seed et la fin du dernier scénario.",
        "",
    ])


def summary_section(runs: dict) -> str:
    has_dotnet = bool(runs.get("dotnet"))
    has_rust = bool(runs.get("rust"))
    lines = ["## Résumé", ""]

    if not has_dotnet and not has_rust:
        lines += ["Aucun résultat exploitable : les deux répertoires de mesures sont vides.", ""]
        return "\n".join(lines)

    if not has_rust or not has_dotnet:
        present = ".NET" if has_dotnet else "Rust"
        missing = "Rust" if has_dotnet else ".NET"
        run = runs.get("dotnet") or runs.get("rust")
        scenarios = run.get("scenarios", {})
        best = max(scenarios.values(), key=lambda s: s.get("requests_per_sec") or 0, default=None)
        worst = min(scenarios.values(), key=lambda s: s.get("requests_per_sec") or 0, default=None)
        errors = sum(int(s.get("errors") or 0) for s in scenarios.values())
        lines += [
            f"Campagne partielle : seul le backend **{present}** a été mesuré, les colonnes "
            f"{missing} sont à `n/a`. "
            + (
                f"Sur {len(scenarios)} scénarios, le débit va de "
                f"{fmt_num((worst or {}).get('requests_per_sec'))} req/s (`{(worst or {}).get('name')}`) à "
                f"{fmt_num((best or {}).get('requests_per_sec'))} req/s (`{(best or {}).get('name')}`), "
                f"pour {errors} erreur(s) au total."
                if scenarios else "Aucun scénario n'a produit de mesure."
            ),
            "",
        ]
        return "\n".join(lines)

    geo = geometric_mean_ratio(runs)
    common = [
        name for name in scenario_names(runs)
        if name in runs["dotnet"]["scenarios"] and name in runs["rust"]["scenarios"]
    ]
    gains = []
    for name in common:
        d_rps = float(runs["dotnet"]["scenarios"][name].get("requests_per_sec") or 0)
        r_rps = float(runs["rust"]["scenarios"][name].get("requests_per_sec") or 0)
        if d_rps > 0 and r_rps > 0:
            gains.append((r_rps / d_rps, name))
    gains.sort()

    dotnet_errors = sum(int(s.get("errors") or 0) for s in runs["dotnet"]["scenarios"].values())
    rust_errors = sum(int(s.get("errors") or 0) for s in runs["rust"]["scenarios"].values())

    if geo is None:
        verdict = "Les deux backends ont été mesurés mais aucun scénario commun n'a produit de débit exploitable."
    else:
        if geo >= 1.05:
            tone = f"Rust soutient en moyenne géométrique **×{geo:.2f}** le débit de .NET"
        elif geo <= 0.95:
            tone = f"Rust soutient en moyenne géométrique **×{geo:.2f}** le débit de .NET (donc en retrait)"
        else:
            tone = f"les deux backends sont au coude à coude (moyenne géométrique ×{geo:.2f})"
        verdict = (
            f"Sur les {len(common)} scénarios communs, {tone}. "
            f"L'écart le plus favorable à Rust est `{gains[-1][1]}` (×{gains[-1][0]:.2f}), "
            f"le moins favorable `{gains[0][1]}` (×{gains[0][0]:.2f}). "
            f"Côté fiabilité, {dotnet_errors} erreur(s) HTTP côté .NET et {rust_errors} côté Rust."
        )

    dotnet_process = runs["dotnet"].get("process", {})
    rust_process = runs["rust"].get("process", {})
    extras = []
    if dotnet_process.get("startup_ms") and rust_process.get("startup_ms"):
        extras.append(
            f"Le démarrage passe de {fmt_num(dotnet_process['startup_ms'])} ms (.NET) à "
            f"{fmt_num(rust_process['startup_ms'])} ms (Rust)"
        )
    if dotnet_process.get("rss_mb_after_scenarios") and rust_process.get("rss_mb_after_scenarios"):
        extras.append(
            f"le RSS en fin de campagne de {fmt_num(dotnet_process['rss_mb_after_scenarios'], 1)} Mo à "
            f"{fmt_num(rust_process['rss_mb_after_scenarios'], 1)} Mo"
        )
    if extras:
        verdict += " " + ", ".join(extras) + "."

    lines += [verdict, ""]
    return "\n".join(lines)


def caveats_section() -> str:
    return "\n".join([
        "## Limites de lecture",
        "",
        "- Les chiffres dépendent de la machine (cœurs, charge concurrente, disque, conteneur).",
        "  Seuls les **rapports** entre les deux backends mesurés dans la même campagne ont du sens ;",
        "  les valeurs absolues ne se comparent pas d'une machine à l'autre.",
        "- Le générateur de charge tourne sur la même machine que le serveur et que PostgreSQL :",
        "  il consomme du CPU, ce qui plafonne les scénarios les plus légers (`health` en premier).",
        "- Les scénarios d'écriture font grossir le jeu de données pendant leur propre run ;",
        "  ils passent en dernier pour ne pas fausser les lectures.",
        "- Le backend .NET embarque Hangfire (jobs récurrents) là où le port Rust utilise une tâche",
        "  `tokio` : une partie du démarrage et de la mémoire de .NET vient de là.",
        "- `login` mesure d'abord bcrypt (cost 11), identique des deux côtés : c'est un plancher",
        "  CPU commun, pas une différence de framework. Le scénario alterne sur plusieurs comptes :",
        "  marteler un seul compte mesurerait la contention sur ses propres sessions (purge",
        "  au-delà de `MaxSessionsPerUser`) plutôt que le coût d'une authentification.",
        "",
    ])


# --------------------------------------------------------------------------- #
# Entrée
# --------------------------------------------------------------------------- #
def copy_raw(runs: dict, raw_dir: Path) -> None:
    for backend in BACKENDS:
        run = runs.get(backend)
        if not run:
            continue
        destination = raw_dir / backend
        if destination.exists():
            shutil.rmtree(destination)
        destination.mkdir(parents=True, exist_ok=True)
        source = Path(run["dir"])
        for item in source.iterdir():
            if item.name.startswith("."):
                continue
            if item.is_dir():
                shutil.copytree(item, destination / item.name)
            else:
                shutil.copy2(item, destination / item.name)
        # Le seed contient un JWT et un mot de passe : on ne les commite pas.
        seed_file = destination / "seed.json"
        if seed_file.is_file():
            try:
                with seed_file.open(encoding="utf-8") as handle:
                    seed = json.load(handle)
                seed["token"] = "<retiré>"
                seed["password"] = "<retiré>"
                # Les comptes du scénario login portent eux aussi un mot de passe.
                for account in seed.get("loginUsers") or []:
                    if isinstance(account, dict) and "password" in account:
                        account["password"] = "<retiré>"
                with seed_file.open("w", encoding="utf-8") as handle:
                    json.dump(seed, handle, indent=2, ensure_ascii=False)
                    handle.write("\n")
            except (OSError, json.JSONDecodeError):
                pass


def main() -> int:
    parser = argparse.ArgumentParser(description="Rapport de comparaison Rust vs .NET")
    parser.add_argument("--dotnet", help="répertoire de résultats du backend .NET")
    parser.add_argument("--rust", help="répertoire de résultats du backend Rust")
    parser.add_argument("--out", default="docs/perf/rust-vs-dotnet.md", help="rapport Markdown à écrire")
    parser.add_argument("--raw-dir", default="docs/perf/raw", help="où recopier les JSON bruts")
    parser.add_argument("--no-copy-raw", action="store_true", help="ne pas recopier les JSON bruts")
    args = parser.parse_args()

    root = Path(__file__).resolve().parent.parent
    runs = {"dotnet": load_run(args.dotnet), "rust": load_run(args.rust)}
    runs = {key: value for key, value in runs.items() if value}

    if not runs:
        print("report: aucun résultat trouvé dans les répertoires fournis", file=sys.stderr)
        return 1

    out_path = Path(args.out)
    if not out_path.is_absolute():
        out_path = root / out_path
    out_path.parent.mkdir(parents=True, exist_ok=True)

    raw_dir = Path(args.raw_dir)
    if not raw_dir.is_absolute():
        raw_dir = root / raw_dir
    if not args.no_copy_raw:
        copy_raw(runs, raw_dir)

    env = environment(root, runs)
    raw_relative = os.path.relpath(raw_dir, out_path.parent)

    document = "\n".join([
        "# Performances : backend Rust vs backend .NET",
        "",
        "Rapport généré par `bench/report.py` à partir des mesures de `bench/run-comparison.sh`.",
        "Ne pas éditer à la main : toute modification est écrasée à la campagne suivante.",
        "",
        "## Environnement",
        "",
        table(["Élément", "Valeur"], [[key, str(value)] for key, value in env.items()]),
        "",
        methodology_section(runs),
        scenario_tables(runs),
        process_table(runs),
        summary_section(runs),
        caveats_section(),
        "## Données brutes",
        "",
        f"Les JSON produits par `oha` et les métriques process sont conservés sous `{raw_relative}/`",
        "(un sous-répertoire par backend : résultats normalisés, sorties brutes du générateur,",
        "`process.json`, `seed.json` expurgé de ses secrets et `server.log`).",
        "",
    ])

    with out_path.open("w", encoding="utf-8") as handle:
        handle.write(re.sub(r"\n{3,}", "\n\n", document))

    print(f"report: {out_path}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
