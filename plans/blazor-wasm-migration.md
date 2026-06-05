# Plan : Migration Frontend → Blazor WebAssembly (CSR) + Radzen

> Référence specs : US-400, US-401, US-402, US-403 (`specs/user-stories.md`, Phase 6)

## Résumé des décisions

| Question | Décision |
|----------|----------|
| Cible | Blazor **WebAssembly** standalone (CSR pur, pas de Blazor Server, pas d'interactivité serveur) |
| Surcouche UI | **Radzen Blazor** (gratuit, 70+ composants, theming intégré) |
| Hébergement | Blob storage public statique (Azure Static Website `$web` / CDN) |
| Logique serveur frontend | **Aucune** — l'API existante reste l'unique backend |
| Doc/API Radzen | Source GitHub `radzenhq/radzen-blazor` + compilateur (pas le MCP payant) |
| Stratégie de bascule | Coexistence : nouveau projet à côté de Next.js, bascule du CI une fois la parité E2E atteinte |
| Auth | Access token (mémoire + localStorage) + refresh cookie HttpOnly (inchangé côté API) |
| i18n | FR/EN client-side (`IStringLocalizer` + `.resx` ou portage des fichiers `messages`) |
| CSP | Statique (hashes) au niveau storage/CDN, plus de nonce par requête |

---

## Pourquoi c'est faisable (validé dans l'environnement)

- L'archi actuelle est **déjà client-side** : `client.ts` (axios) tape l'API directement, pas de BFF/proxy.
- Auth déjà compatible CSR : access token localStorage/mémoire + refresh cookie HttpOnly `withCredentials`.
- La logique « serveur » de Next est minime et déplaçable : nonce CSP (`middleware.ts`), i18n SSR (next-intl), lecture cookie dans `layout.tsx`.
- Outillage présent : **dotnet 10.0.108**, **NuGet joignable**, **source Radzen lisible** sur `raw.githubusercontent.com`.

## Points de friction « blob storage / zéro serveur » et parades

| Logique serveur actuelle | Parade Blazor WASM statique |
|---|---|
| Nonce CSP par requête (`middleware.ts`) | CSP **statique** par hashes, posée au niveau storage/CDN |
| i18n SSR + routing `/fr` `/en` (next-intl) | i18n client (`IStringLocalizer`/`.resx`), routing Blazor client |
| `API_URL` lu au runtime (`layout.tsx`) | `appsettings.json` statique fetché au démarrage |
| Routing SPA | Document d'erreur 404 du blob → `index.html` (Azure Static Website) |
| Cookie refresh cross-domain | `SameSite=None; Secure` + CORS API pour l'origine du blob |
| Poids du payload WASM | Brotli + lazy-loading des assemblies + trimming/AOT |

---

## Inventaire de parité (US-401)

Features frontend réellement implémentées à reproduire :

- **Auth** : `register`, `login`, logout, garde de redirection, refresh interceptor
- **Maisons** : liste, `houses/new`, `houses/[id]`, edit dialog, delete dialog, `members-section`
- **Appareils** : liste, `devices/[id]`, `houses/[id]/devices/new`, edit dialog, delete dialog
- **Entretien** : `add-maintenance-type` dialog, `log-maintenance` dialog, échéances/calculs
- **Dashboard** : vue d'ensemble + indicateurs
- **Invitations** : liste, acceptation par token (`invitations/[token]`)
- **Membres & rôles** : permissions partagées
- **Paramètres** : `settings`
- **Transverse** : i18n FR/EN, thème clair/sombre, toasts, dialogs, formulaires validés, client API typé (OpenAPI), cache de données

---

## Phasage

### Phase 0 — Cadrage & POC (valider l'archi avant de tout porter)
1. Créer `src/HouseFlow.Frontend.Wasm` (`dotnet new blazorwasm`), ajouter `Radzen.Blazor`.
2. Générer le **client API typé** depuis `specs/openapi.yaml` (NSwag, déjà utilisé côté backend) → DTO/clients C#.
3. POC : **login + liste des maisons** end-to-end (auth réelle + appel API + composant Radzen).
4. Valider : CORS, refresh token cross-origin, theming Radzen, build `dotnet publish` → `wwwroot` statique servi.
5. **Go/No-Go** avec l'utilisateur.

### Phase 1 — Fondations
- Layout/shell Radzen (header, nav, toggle thème, toggle langue).
- `appsettings.json` runtime config (API_URL) + chargement au démarrage.
- Service d'auth (token store, `AuthenticationStateProvider`, garde de routes, `DelegatingHandler` pour Bearer + refresh sur 401).
- i18n FR/EN (portage des `messages/*.json` → ressources).

### Phase 2 — Portage des modules (vue par vue, avec tests)
Ordre : Auth → Maisons → Appareils → Entretien → Dashboard → Invitations → Membres/Rôles → Paramètres.
Chaque module : composants Radzen + appels API + tests (bUnit unit + scénarios E2E Playwright réutilisés).

### Phase 3 — Sécurité & i18n sans serveur (US-403)
- CSP statique (hashes) + en-têtes au niveau storage/CDN.
- Vérifier parité sécurité vs CSP actuelle (cf. `SECURITY.md`).

### Phase 4 — Déploiement statique
- Pipeline : `dotnet publish` → upload `wwwroot` vers blob `$web` + invalidation CDN.
- Document d'erreur 404 → `index.html` (fallback SPA).
- Brotli + cache headers (immutable pour assets hashés).

### Phase 5 — Bascule & nettoyage
- Faire tourner les **4 étapes de vérif obligatoires** (dotnet test, vitest le temps de la coexistence, build, E2E) — E2E réorientés vers le nouveau frontend.
- Une fois la parité E2E verte : retirer `src/HouseFlow.Frontend` (Next.js) du build/CI, MAJ `PROJECT_KNOWLEDGE.md` et `README.md`.

---

## Tests / Vérification

- **bUnit** pour les composants Blazor (remplace vitest/RTL).
- **Playwright E2E** : réutiliser les scénarios existants (`e2e/`) en les pointant sur le nouveau frontend — c'est le filet de sécurité anti-régression.
- Checklist projet avant tout push : `dotnet test`, build, `scripts/verify-e2e.sh`.

## Risques

| Risque | Mitigation |
|---|---|
| Poids/temps de premier chargement WASM | Brotli, lazy-load, trimming, AOT ; mesurer vs Next |
| Cookie refresh cross-site bloqué (navigateurs) | `SameSite=None; Secure`, CORS strict, tester Safari/ITP |
| Parité i18n/SEO | App authentifiée (pas de besoin SEO) ; i18n client suffit |
| Effort de réécriture (82 fichiers TSX) | Phasage par module + POC de validation amont |
| Equivalent Radzen manquant pour un composant Radix | Vérifier au POC ; fallback composant custom |

---

## Hors périmètre

- Aucune modification du **backend API** (contrat `openapi.yaml` inchangé).
- Pas de Blazor Server ni d'interactivité serveur.
