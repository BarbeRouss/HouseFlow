import { test, expect, Page } from '@playwright/test';
import { execFileSync } from 'child_process';

/**
 * RGPD — acceptation des CGU à l'inscription (issue #135) et pages légales (issue #134).
 *
 * Rappel : la case à cocher est une acceptation CONTRACTUELLE des Conditions générales
 * d'utilisation (base légale : exécution du contrat, art. 6(1)(b)). La politique de
 * confidentialité fait l'objet d'une simple mention de prise de connaissance (art. 13),
 * sans case — ce n'est pas un consentement au sens de l'art. 6(1)(a).
 */

const uniqueEmail = () => `gdpr-${Date.now()}-${Math.random().toString(36).slice(2, 8)}@houseflow.test`;
// Version de politique en vigueur (GdprPolicy.CurrentPolicyVersion / LegalConstants.PolicyVersion).
// Codée en dur dans quatre attentes auparavant : chaque incrément de politique cassait la suite
// alors que le produit était sain.
const POLICY_VERSION = process.env.POLICY_VERSION || '2026-09-23';

const PASSWORD = 'TestPassword123!';

async function fillRegistrationForm(page: Page, email: string) {
  const firstName = page.getByPlaceholder('Jean');
  await firstName.click();
  await firstName.pressSequentially('Jean', { delay: 30 });

  const lastName = page.getByPlaceholder('Dupont');
  await lastName.click();
  await lastName.pressSequentially('Dupont', { delay: 30 });

  const emailField = page.getByPlaceholder('you@example.com');
  await emailField.click();
  await emailField.pressSequentially(email, { delay: 30 });

  const password = page.locator('input[type="password"]');
  await password.click();
  await password.pressSequentially(PASSWORD, { delay: 30 });
}

test.describe('Acceptation des CGU à l\'inscription', () => {
  test('Le bouton d\'inscription est désactivé tant que les CGU ne sont pas acceptées', async ({ page }) => {
    await page.goto('/fr/register');
    await page.waitForLoadState('networkidle');

    const checkbox = page.locator('#acceptTerms');
    const submit = page.getByRole('button', { name: /s'inscrire|sign up/i });

    // La case n'est JAMAIS pré-cochée (considérant 32 / checklist B.21).
    await expect(checkbox).not.toBeChecked();
    await expect(submit).toBeDisabled();

    await fillRegistrationForm(page, uniqueEmail());

    // Formulaire complet mais CGU non acceptées : toujours bloqué.
    await expect(submit).toBeDisabled();

    await checkbox.check();
    await expect(submit).toBeEnabled();
  });

  test('Les liens vers les CGU et la politique sont présents et ouvrent un nouvel onglet', async ({ page }) => {
    await page.goto('/fr/register');
    await page.waitForLoadState('networkidle');

    const termsLink = page.locator('label[for="acceptTerms"] a');
    await expect(termsLink).toHaveAttribute('href', '/fr/terms');
    await expect(termsLink).toHaveAttribute('target', '_blank');

    // Mention Art. 13 : information, présentée SANS case à cocher.
    const privacyLink = page.locator('form a[href="/fr/privacy"]');
    await expect(privacyLink).toBeVisible();
    await expect(privacyLink).toHaveAttribute('target', '_blank');
    await expect(page.getByText(/reconnaissez avoir pris connaissance/i)).toBeVisible();
  });

  test('L\'inscription aboutit une fois les CGU acceptées', async ({ page }) => {
    await page.goto('/fr/register');
    await page.waitForLoadState('networkidle');

    await fillRegistrationForm(page, uniqueEmail());
    await page.locator('#acceptTerms').check();
    await page.getByRole('button', { name: /s'inscrire|sign up/i }).click();

    await page.waitForURL(/\/fr\/houses\/[^/]+\/devices\/new/, { timeout: 15000 });
  });

  test('Un nouvel inscrit ne voit PAS la bannière de ré-acceptation', async ({ page }) => {
    await page.goto('/fr/register');
    await page.waitForLoadState('networkidle');

    await fillRegistrationForm(page, uniqueEmail());
    await page.locator('#acceptTerms').check();
    await page.getByRole('button', { name: /s'inscrire|sign up/i }).click();

    await page.waitForURL(/\/fr\/houses\/[^/]+\/devices\/new/, { timeout: 15000 });

    // consentRequired est false pour un compte qui vient d'accepter la version en vigueur.
    // Le cas positif (bannière affichée) est couvert plus bas, « Bannière de ré-acceptation ».
    await expect(page.locator('#acceptUpdatedTerms')).toHaveCount(0);
  });
});

test.describe('Bannière de ré-acceptation', () => {
  /**
   * Remet un compte dans l'état « n'a jamais accepté » — celui des comptes créés avant
   * l'introduction des documents versionnés. Aucune API ne permet de revenir en arrière
   * (ce serait un endpoint dangereux et sans usage produit), d'où le passage par la base.
   */
  function clearConsent(email: string) {
    execFileSync('psql', [
      '-h', process.env.POSTGRES_HOST || 'localhost',
      '-U', 'postgres',
      '-d', process.env.DB_NAME || 'houseflow',
      '-c', `UPDATE "Users" SET "ConsentGivenAt" = NULL, "ConsentPolicyVersion" = NULL WHERE "Email" = '${email}';`,
    ], { env: { ...process.env, PGPASSWORD: 'postgres' }, stdio: 'pipe' });
  }

  test('Un compte sans acceptation voit la bannière, et elle disparaît après acceptation', async ({ page, request }) => {
    const API_URL = process.env.API_URL || 'http://localhost:5203';
    const email = uniqueEmail();

    const registered = await request.post(`${API_URL}/api/v1/auth/register`, {
      data: { firstName: 'Legacy', lastName: 'User', email, password: PASSWORD, consentAccepted: true },
    });
    expect(registered.ok()).toBeTruthy();

    clearConsent(email);

    await page.goto('/fr/login');
    await page.waitForLoadState('networkidle');
    const emailField = page.getByPlaceholder('you@example.com');
    await emailField.click();
    await emailField.pressSequentially(email, { delay: 30 });
    const passwordField = page.locator('input[type="password"]');
    await passwordField.click();
    await passwordField.pressSequentially(PASSWORD, { delay: 30 });
    await page.getByRole('button', { name: /se connecter|login/i }).click();

    await page.waitForURL(/\/fr\/(dashboard|houses\/[a-f0-9-]+)$/, { timeout: 15000 });

    // La bannière annonce la version en vigueur et propose les deux documents.
    const accept = page.locator('#acceptUpdatedTerms');
    await expect(accept).toBeVisible();
    await expect(page.getByText(POLICY_VERSION).first()).toBeVisible();

    // Non bloquante : le contenu de l'application reste accessible derrière (pas d'overlay).
    await expect(page.locator('header')).toBeVisible();

    await accept.click();

    // Elle disparaît sans rechargement, et ne revient pas après navigation.
    await expect(accept).toHaveCount(0);
    await page.reload();
    await page.waitForLoadState('networkidle');
    await expect(page.locator('#acceptUpdatedTerms')).toHaveCount(0);
  });
});

test.describe('Pages légales accessibles sans authentification', () => {
  test('La politique de confidentialité s\'affiche en français', async ({ page }) => {
    await page.goto('/fr/privacy');
    await page.waitForLoadState('networkidle');

    await expect(page.getByRole('heading', { name: /politique de confidentialité/i, level: 1 })).toBeVisible();
    // Date / version du document (checklist A.14).
    await expect(page.getByText(POLICY_VERSION).first()).toBeVisible();
    // Sections clés (checklist A.9, A.10, A.13).
    await expect(page.getByRole('heading', { name: /vos droits/i, level: 2 })).toBeVisible();
    await expect(page.getByRole('heading', { name: /cookies et traceurs/i, level: 2 })).toBeVisible();
    await expect(page.getByText(/CNIL/).first()).toBeVisible();
    await expect(page.getByText(/Autorité de protection des données/i).first()).toBeVisible();
  });

  test('La politique de confidentialité s\'affiche en anglais', async ({ page }) => {
    await page.goto('/en/privacy');
    await page.waitForLoadState('networkidle');

    await expect(page.getByRole('heading', { name: /privacy policy/i, level: 1 })).toBeVisible();
    await expect(page.getByText(POLICY_VERSION).first()).toBeVisible();
    await expect(page.getByRole('heading', { name: /your rights/i, level: 2 })).toBeVisible();
    await expect(page.getByRole('heading', { name: /cookies and trackers/i, level: 2 })).toBeVisible();
  });

  test('Les conditions générales d\'utilisation s\'affichent en français', async ({ page }) => {
    await page.goto('/fr/terms');
    await page.waitForLoadState('networkidle');

    await expect(page.getByRole('heading', { name: /conditions générales d'utilisation/i, level: 1 })).toBeVisible();
    await expect(page.getByText(POLICY_VERSION).first()).toBeVisible();
    await expect(page.getByRole('heading', { name: /votre compte/i, level: 2 })).toBeVisible();
  });

  test('Le pied de page de la connexion mène aux pages légales', async ({ page }) => {
    await page.goto('/fr/login');
    await page.waitForLoadState('networkidle');

    const footer = page.locator('footer');
    await expect(footer).toBeVisible();
    await expect(footer.getByRole('link', { name: /politique de confidentialité/i })).toHaveAttribute('href', '/fr/privacy');
    await expect(footer.getByRole('link', { name: /conditions d'utilisation/i })).toHaveAttribute('href', '/fr/terms');
    await expect(footer.getByRole('link', { name: /privacy@houseflow\.cloud/i })).toBeVisible();

    // Depuis le pied de page, la politique s'ouvre sans être authentifié.
    await footer.getByRole('link', { name: /politique de confidentialité/i }).click();
    await page.waitForURL(/\/fr\/privacy/);
    await expect(page.getByRole('heading', { name: /politique de confidentialité/i, level: 1 })).toBeVisible();
  });
});
