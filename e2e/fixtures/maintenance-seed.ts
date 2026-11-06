import { APIRequestContext, Page, expect } from '@playwright/test';
import { addRefreshCookie, createHouseViaApi, registerViaApi } from './auth';

/**
 * API seeding for the maintenance pages (P07 dashboard, P10 device): build the data through the
 * API, then log the browser in with the refresh cookie — the tests exercise the pages under test,
 * not the onboarding UI.
 */
export const API_URL = process.env.API_URL || `http://localhost:${process.env.API_PORT || 5203}`;

export type Session = { token: string; refreshCookie: string; userId: string };

export type LastMaintenance =
  | { kind: 'Unknown' }
  | { kind: 'Older' }
  | { kind: 'Month'; year: number; month: number };

/** « Dernier entretien » n months ago (month precision): the API records it on the 1st of that month. */
export function monthsAgo(n: number): LastMaintenance {
  const d = new Date();
  d.setDate(1);
  d.setMonth(d.getMonth() - n);
  return { kind: 'Month', year: d.getFullYear(), month: d.getMonth() + 1 };
}

/** yyyy-mm-dd, n days ago. */
export function isoDaysAgo(n: number): string {
  const d = new Date();
  d.setDate(d.getDate() - n);
  return d.toISOString().split('T')[0];
}

function auth(token: string) {
  return { Authorization: `Bearer ${token}` };
}

export async function registerUser(request: APIRequestContext, firstName = 'Maint', lastName = 'Tester'): Promise<Session> {
  const user = await registerViaApi(request, { firstName, lastName });
  return { token: user.token, refreshCookie: user.refreshCookie, userId: user.userId };
}

export async function createHouse(request: APIRequestContext, s: Session, name = 'Maison des Lilas'): Promise<string> {
  return createHouseViaApi(request, s.token, name);
}

export async function createDevice(
  request: APIRequestContext,
  s: Session,
  houseId: string,
  device: { name: string; type?: string; brand?: string; model?: string; installDate?: string },
): Promise<string> {
  const res = await request.post(`${API_URL}/api/v1/houses/${houseId}/devices`, {
    headers: auth(s.token),
    data: { type: 'Autre', ...device },
  });
  expect(res.ok(), `create device: HTTP ${res.status()}`).toBeTruthy();
  return (await res.json()).id;
}

export async function createType(
  request: APIRequestContext,
  s: Session,
  deviceId: string,
  type: { name: string; periodicity?: string; customMonths?: number; lastMaintenance?: LastMaintenance },
): Promise<string> {
  const res = await request.post(`${API_URL}/api/v1/devices/${deviceId}/maintenance-types`, {
    headers: auth(s.token),
    data: { periodicity: 'Annual', lastMaintenance: { kind: 'Unknown' }, ...type },
  });
  expect(res.ok(), `create maintenance type: HTTP ${res.status()}`).toBeTruthy();
  return (await res.json()).id;
}

export async function logRecord(
  request: APIRequestContext,
  s: Session,
  typeId: string,
  record: { date: string; cost?: number; provider?: string; notes?: string },
): Promise<string> {
  const res = await request.post(`${API_URL}/api/v1/maintenance-types/${typeId}/instances`, {
    headers: auth(s.token),
    data: { ...record, date: `${record.date}T00:00:00.000Z` },
  });
  expect(res.ok(), `log maintenance: HTTP ${res.status()}`).toBeTruthy();
  return (await res.json()).id;
}

/** Logs the browser in as this user and opens the given app path (e.g. /fr/dashboard). */
export async function openAs(page: Page, s: Session, path: string) {
  await addRefreshCookie(page.context(), s.refreshCookie);
  await page.goto(path);
}
