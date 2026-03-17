/**
 * Translation completeness checker.
 *
 * Verifies that:
 * 1. All keys in en.json exist in fr.json (and vice versa)
 * 2. No translation values are empty
 * 3. No .tsx files contain obvious hardcoded UI strings (heuristic)
 *
 * Run: npx tsx scripts/check-translations.ts
 */

import { readFileSync, readdirSync, statSync } from 'fs';
import { join, relative } from 'path';

const MESSAGES_DIR = join(__dirname, '..', 'src', 'messages');
const SRC_DIR = join(__dirname, '..', 'src');

// ── 1. Load translation files ───────────────────────────────────────────────

function loadJson(path: string): Record<string, unknown> {
  return JSON.parse(readFileSync(path, 'utf-8'));
}

function flattenKeys(obj: Record<string, unknown>, prefix = ''): string[] {
  const keys: string[] = [];
  for (const [key, value] of Object.entries(obj)) {
    const fullKey = prefix ? `${prefix}.${key}` : key;
    if (typeof value === 'object' && value !== null && !Array.isArray(value)) {
      keys.push(...flattenKeys(value as Record<string, unknown>, fullKey));
    } else {
      keys.push(fullKey);
    }
  }
  return keys;
}

function getNestedValue(obj: Record<string, unknown>, path: string): unknown {
  const parts = path.split('.');
  let current: unknown = obj;
  for (const part of parts) {
    if (current === null || current === undefined || typeof current !== 'object') return undefined;
    current = (current as Record<string, unknown>)[part];
  }
  return current;
}

// ── 2. Compare translation files ────────────────────────────────────────────

const en = loadJson(join(MESSAGES_DIR, 'en.json'));
const fr = loadJson(join(MESSAGES_DIR, 'fr.json'));

const enKeys = flattenKeys(en).sort();
const frKeys = flattenKeys(fr).sort();

let errors = 0;

// Check for keys missing in fr
const missingInFr = enKeys.filter(k => !frKeys.includes(k));
if (missingInFr.length > 0) {
  console.error('\n❌ Keys present in en.json but MISSING in fr.json:');
  missingInFr.forEach(k => console.error(`   - ${k}`));
  errors += missingInFr.length;
}

// Check for keys missing in en
const missingInEn = frKeys.filter(k => !enKeys.includes(k));
if (missingInEn.length > 0) {
  console.error('\n❌ Keys present in fr.json but MISSING in en.json:');
  missingInEn.forEach(k => console.error(`   - ${k}`));
  errors += missingInEn.length;
}

// Check for empty values
for (const key of enKeys) {
  const val = getNestedValue(en, key);
  if (typeof val === 'string' && val.trim() === '') {
    console.error(`❌ Empty value in en.json: ${key}`);
    errors++;
  }
}
for (const key of frKeys) {
  const val = getNestedValue(fr, key);
  if (typeof val === 'string' && val.trim() === '') {
    console.error(`❌ Empty value in fr.json: ${key}`);
    errors++;
  }
}

// Check for identical values (possible untranslated strings)
// Skip keys that are expected to be the same (e.g., Email, Date, HouseFlow, etc.)
const EXPECTED_SAME = new Set(['auth.email', 'maintenance.date', 'maintenance.notes', 'maintenance.cost']);
const identicalWarnings: string[] = [];
for (const key of enKeys) {
  if (EXPECTED_SAME.has(key)) continue;
  const enVal = getNestedValue(en, key);
  const frVal = getNestedValue(fr, key);
  if (typeof enVal === 'string' && typeof frVal === 'string' && enVal === frVal && enVal.length > 3) {
    identicalWarnings.push(key);
  }
}
if (identicalWarnings.length > 0) {
  console.warn('\n⚠️  Keys with IDENTICAL values in en.json and fr.json (possible untranslated):');
  identicalWarnings.forEach(k => {
    console.warn(`   - ${k}: "${getNestedValue(en, k)}"`);
  });
}

// ── 3. Scan .tsx files for hardcoded strings (heuristic) ────────────────────

function walkDir(dir: string): string[] {
  const files: string[] = [];
  for (const entry of readdirSync(dir)) {
    const fullPath = join(dir, entry);
    const stat = statSync(fullPath);
    if (stat.isDirectory()) {
      // Skip node_modules, .next, e2e
      if (['node_modules', '.next', 'e2e'].includes(entry)) continue;
      files.push(...walkDir(fullPath));
    } else if (entry.endsWith('.tsx')) {
      files.push(fullPath);
    }
  }
  return files;
}

// Patterns that suggest hardcoded user-facing strings in JSX
// Match: >Some text< or >"Some text"< but not code-like patterns
const HARDCODED_PATTERNS = [
  // Direct text in JSX (not inside {})
  />\s*[A-Z][a-zà-ÿ]+(?: [a-zà-ÿA-ZÀ-Ÿ]+){2,}\s*</g,
];

// Files/directories to skip
const SKIP_PATHS = ['components/ui/skeleton', 'components/ui/button', 'components/ui/card', 'layout.tsx'];

const tsxFiles = walkDir(SRC_DIR);
const hardcodedWarnings: { file: string; line: number; text: string }[] = [];

for (const file of tsxFiles) {
  const relPath = relative(SRC_DIR, file);
  if (SKIP_PATHS.some(s => relPath.includes(s))) continue;

  const content = readFileSync(file, 'utf-8');
  const lines = content.split('\n');

  for (let i = 0; i < lines.length; i++) {
    const line = lines[i];
    // Skip comment lines, import lines, className attributes
    if (line.trim().startsWith('//') || line.trim().startsWith('import ') || line.includes('className')) continue;
    if (line.includes('placeholder=') && !line.includes('{')) {
      // hardcoded placeholder
      const match = line.match(/placeholder="([^"]+)"/);
      if (match && match[1].length > 5 && /[a-zA-Z]/.test(match[1])) {
        hardcodedWarnings.push({ file: relPath, line: i + 1, text: `placeholder="${match[1]}"` });
      }
    }
  }
}

if (hardcodedWarnings.length > 0) {
  console.warn('\n⚠️  Possible hardcoded strings found in .tsx files:');
  hardcodedWarnings.forEach(w => {
    console.warn(`   - ${w.file}:${w.line} → ${w.text}`);
  });
}

// ── 4. Summary ──────────────────────────────────────────────────────────────

console.log('\n────────────────────────────────────────');
console.log(`Translation keys: en=${enKeys.length}, fr=${frKeys.length}`);

if (errors > 0) {
  console.error(`\n❌ ${errors} error(s) found. Fix them before deploying.`);
  process.exit(1);
} else {
  console.log('\n✅ All translation keys are in sync!');
  if (identicalWarnings.length > 0 || hardcodedWarnings.length > 0) {
    console.log(`   (${identicalWarnings.length} identical-value warnings, ${hardcodedWarnings.length} possible hardcoded strings)`);
  }
  process.exit(0);
}
