# Security Documentation

## Overview

This document outlines the security measures implemented in the HouseFlow application to protect user data and prevent common vulnerabilities.

## Security Implementations

### Phase 1: Critical Security Fixes (COMPLETED)

#### 1.1 Secrets Management ✅

**Issue**: Sensitive credentials (JWT keys, database passwords) were hardcoded in configuration files committed to version control.

**Solution**:
- Moved all secrets to environment variables and development-only configuration files
- Created `appsettings.Template.json` as a template for production configuration
- Added sensitive files to `.gitignore`
- Implemented JWT key strength validation (minimum 256 bits)

**Configuration Priority**:
1. Environment variables (e.g., `JWT__KEY`)
2. Configuration files (`appsettings.Development.json`)
3. User secrets (for local development)

**Production Setup**:
```bash
# Set environment variables
export JWT__KEY="your-secure-key-minimum-32-characters-required"
export ConnectionStrings__DefaultConnection="your-database-connection-string"
```

#### 1.2 Input Validation ✅

**Issue**: No server-side validation of user inputs, allowing potentially malicious data.

**Solution**:
- Implemented DataAnnotations validation on all DTOs
- Added comprehensive validation rules:
  - Email format validation
  - String length limits
  - Regular expression patterns for complex fields
  - Enum validation
  - Range validation for numeric fields

**Examples**:
- Email: Valid format, max 255 characters
- Passwords: 12-255 characters, complexity requirements
- Names: 1-255 characters
- Zip codes: Alphanumeric pattern validation

#### 1.3 Rate Limiting ✅

**Issue**: No protection against brute force attacks or API abuse.

**Solution**:
- Implemented tiered rate limiting:
  - **Auth endpoints**: 5 requests/minute (prevents brute force)
  - **API endpoints**: 100 requests/minute
  - **Global fallback**: 200 requests/minute
- Returns HTTP 429 (Too Many Requests) with retry-after information
- Partitioned by user identity or host
- **Note**: Disabled in Development and Testing environments to allow E2E tests

**Production Activation**:
Rate limiting is automatically enabled in Production and Staging environments.

#### 1.4 Password Policy ✅

**Issue**: Weak password requirements (6 characters) allowed easily guessable passwords.

**Solution**:
- Minimum length: 8 characters
- Complexity requirements:
  - At least one digit
- Pattern enforced both client-side (HTML5) and server-side (DataAnnotations + RegularExpression)
- Passwords hashed with BCrypt (work factor: default)

**Password Regex**:
```regex
^(?=.*\d).{8,}$
```

### Phase 2: Important Security Improvements (COMPLETED)

#### 2.1 Security Headers ✅

**Issue**: Missing security headers left the application vulnerable to various attacks.

**Solution**:
Implemented `SecurityHeadersMiddleware` with the following headers:

| Header | Value | Purpose |
|--------|-------|---------|
| `X-Content-Type-Options` | `nosniff` | Prevent MIME type sniffing |
| `X-Frame-Options` | `DENY` | Prevent clickjacking |
| `X-XSS-Protection` | `1; mode=block` | Enable browser XSS protection |
| `Referrer-Policy` | `strict-origin-when-cross-origin` | Control referrer information |
| `Permissions-Policy` | Restricted features | Limit browser API access |
| `Content-Security-Policy` | Strict policy | Prevent XSS and injection attacks |
| `Strict-Transport-Security` | `max-age=31536000` | Enforce HTTPS (HTTPS only) |

**CSP Policy**:
```
default-src 'self';
script-src 'self' 'unsafe-inline' 'unsafe-eval';
style-src 'self' 'unsafe-inline';
img-src 'self' data: https:;
font-src 'self';
connect-src 'self' http://localhost:3000 http://localhost:5203;
frame-ancestors 'none'
```

#### 2.2 Security Logging ✅

**Issue**: No audit trail for authentication events, making security investigations difficult.

**Solution**:
- Added structured logging to `AuthService` for:
  - Registration attempts (with email)
  - Registration failures (email already exists)
  - Successful registrations (user ID and email)
  - Login attempts (with email)
  - Login failures (user not found, invalid password)
  - Successful logins (user ID)
- Generic error messages prevent email enumeration
- All authentication events logged with appropriate severity levels

**Log Levels**:
- `Information`: Successful operations
- `Warning`: Failed attempts (security relevant)
- `Error`: System errors

#### 2.3 Pagination Infrastructure ✅

**Issue**: Unbounded queries could lead to performance issues and potential DoS.

**Solution**:
- Created `PagedResult<T>` generic class for consistent pagination
- Created `PaginationParams` with configurable limits:
  - Default page size: 20
  - Maximum page size: 100 (prevents abuse)
  - Calculated skip/take values
  - Navigation properties (HasNextPage, HasPreviousPage)

**Usage Example**:
```csharp
public async Task<PagedResult<DeviceDto>> GetDevicesAsync(
    Guid houseId,
    PaginationParams pagination)
{
    var query = _context.Devices.Where(d => d.HouseId == houseId);
    var totalCount = await query.CountAsync();
    var devices = await query
        .Skip(pagination.Skip)
        .Take(pagination.PageSize)
        .ToListAsync();

    return new PagedResult<DeviceDto>(
        devices.Select(MapToDto),
        pagination.Page,
        pagination.PageSize,
        totalCount
    );
}
```

### Phase 3: Advanced Security (DEFERRED)

The following features are planned but not yet implemented:

#### 3.1 Email Verification (TODO)
- Require users to verify email addresses before account activation
- Generate secure verification tokens
- Implement verification email sending
- Set token expiration (e.g., 24 hours)

#### 3.2 Two-Factor Authentication (2FA) (TODO)
- Support TOTP-based 2FA (authenticator apps)
- QR code generation for easy setup
- Backup codes for account recovery
- Optional enforcement for sensitive operations

#### 3.3 Password Reset Flow (TODO)
- Secure password reset token generation
- Token expiration and single-use enforcement
- Email-based reset flow
- Rate limiting on reset requests

#### 3.4 Soft Delete (TODO)
- Implement soft delete for critical entities
- Allow data recovery within a time window
- Audit trail preservation
- Permanent deletion after retention period

#### 3.5 Complete Audit Trail (TODO)
- Track all data modifications (who, what, when)
- Immutable audit log
- Compliance with data protection regulations
- Query and export capabilities

#### 3.6 HttpOnly Cookies for JWT (TODO)
- Move tokens from localStorage to HttpOnly cookies
- Prevent XSS-based token theft
- Implement secure cookie configuration
- CSRF protection required

#### 3.7 Refresh Tokens (TODO)
- Implement refresh token rotation
- Short-lived access tokens (15 minutes)
- Long-lived refresh tokens (7 days)
- Token family tracking for security

#### 3.8 CSRF Protection (TODO)
- Anti-forgery tokens for state-changing operations
- Double-submit cookie pattern
- SameSite cookie attribute
- Validation middleware

## Best Practices

### For Developers

1. **Never commit secrets**: Use environment variables or user secrets
2. **Validate all inputs**: Use DataAnnotations and custom validation
3. **Use parameterized queries**: Entity Framework prevents SQL injection by default
4. **Log security events**: Use structured logging for audit trails
5. **Keep dependencies updated**: Regularly update NuGet packages
6. **Review security headers**: Ensure CSP allows only necessary resources
7. **Test security features**: Include security testing in E2E tests

### For Deployment

1. **Set strong JWT keys**: Minimum 32 characters, cryptographically random
2. **Use HTTPS in production**: Enforce with HSTS headers
3. **Configure CORS properly**: Limit allowed origins to your domains
4. **Enable rate limiting**: Ensure not in Development mode
5. **Monitor logs**: Set up alerts for suspicious activities
6. **Regular backups**: Implement automated database backups
7. **Security scanning**: Use tools like OWASP Dependency-Check

## Security Checklist

### Pre-Deployment

- [ ] All secrets moved to environment variables
- [ ] Strong JWT key configured (32+ characters)
- [ ] HTTPS enabled and enforced
- [ ] CORS configured with production origins only
- [ ] Rate limiting enabled (not in Development mode)
- [ ] Security headers middleware active
- [ ] Database connection string secured
- [ ] Error messages don't leak sensitive information
- [ ] Security logging configured
- [ ] Backup and recovery procedures tested

### Post-Deployment

- [ ] Monitor authentication logs for suspicious activity
- [ ] Review rate limit rejections regularly
- [ ] Keep all dependencies up to date
- [ ] Perform regular security audits
- [ ] Test incident response procedures

## Vulnerability Reporting

If you discover a security vulnerability in HouseFlow, please report it to:

**Email**: security@houseflow.example.com

**Please include**:
- Description of the vulnerability
- Steps to reproduce
- Potential impact
- Suggested fix (if applicable)

We appreciate responsible disclosure and will respond promptly to security reports.

## Compliance

HouseFlow implements security measures to support compliance with:

- **GDPR**: Data protection, right to erasure (when soft delete is implemented)
- **OWASP Top 10**: Protection against common web vulnerabilities
- **Password Security**: Following NIST guidelines for password strength

## Version History

| Version | Date | Changes |
|---------|------|---------|
| 1.0.0 | 2025-12-30 | Initial security implementation (Phases 1 & 2) |

## References

- [OWASP Top 10](https://owasp.org/www-project-top-ten/)
- [NIST Password Guidelines](https://pages.nist.gov/800-63-3/sp800-63b.html)
- [ASP.NET Core Security](https://docs.microsoft.com/en-us/aspnet/core/security/)
- [JWT Best Practices](https://datatracker.ietf.org/doc/html/rfc8725)
