import { execFileSync } from 'child_process';

/**
 * Direct SQL on the API's database, for states no endpoint can produce (e.g. RGPD Art. 18
 * restriction, which is applied from the bastion — docs/gdpr/rights-requests-log.md).
 * Uses the `psql` client: present in the devcontainer (POSTGRES_HOST=postgres) and on the CI
 * runner (Postgres service on localhost). Override with E2E_DB_HOST / E2E_DB_NAME if needed.
 */
function psql(sql: string): string {
  return execFileSync('psql', [
    '-h', process.env.E2E_DB_HOST || process.env.POSTGRES_HOST || 'localhost',
    '-U', process.env.E2E_DB_USER || 'postgres',
    '-d', process.env.E2E_DB_NAME || 'houseflow',
    '-v', 'ON_ERROR_STOP=1', '-tAc', sql,
  ], { env: { ...process.env, PGPASSWORD: process.env.E2E_DB_PASSWORD || 'postgres' }, encoding: 'utf8' });
}

const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/** Puts the account under processing restriction (login and refresh answer `account_restricted`). */
export function restrictAccount(userId: string): void {
  if (!UUID.test(userId)) throw new Error(`restrictAccount: not a user id: ${userId}`);
  const updated = psql(
    `UPDATE "Users" SET "ProcessingRestrictedAt" = NOW() AT TIME ZONE 'UTC' WHERE "Id" = '${userId}' RETURNING 1;`);
  if (!updated.split('\n').some(line => line.trim() === '1')) throw new Error(`restrictAccount: user ${userId} not found`);
}
