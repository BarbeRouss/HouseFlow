/**
 * Module-level flag to coordinate redirects between auth forms and the auth layout.
 *
 * When a login/register form authenticates the user, it sets this flag BEFORE
 * calling router.replace(). The auth layout checks this flag and skips its own
 * redirect to /dashboard, letting the form redirect to its intended destination
 * (e.g. /houses/{id}/devices/new after registration).
 *
 * On back-button navigation, the flag is already cleared, so the layout
 * redirects authenticated users to /dashboard as expected.
 */
let _formIsRedirecting = false;

export function setFormRedirecting() {
  _formIsRedirecting = true;
}

export function consumeFormRedirecting(): boolean {
  if (_formIsRedirecting) {
    _formIsRedirecting = false;
    return true;
  }
  return false;
}
