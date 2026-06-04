import Link from "next/link";

/**
 * Root-level 404 page. Shown when no locale segment matches either.
 * Uses no i18n since we're outside the locale layout.
 */
export default function RootNotFound() {
  return (
    <html lang="en">
      <body
        style={{
          margin: 0,
          fontFamily:
            '-apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif',
          display: "flex",
          alignItems: "center",
          justifyContent: "center",
          minHeight: "100vh",
          backgroundColor: "#f9fafb",
          color: "#111827",
        }}
      >
        <div style={{ textAlign: "center", maxWidth: 420, padding: 32 }}>
          <p
            style={{
              fontSize: 64,
              fontWeight: 700,
              color: "#d1d5db",
              margin: "0 0 8px",
              lineHeight: 1,
            }}
          >
            404
          </p>
          <h1 style={{ fontSize: 20, fontWeight: 600, marginBottom: 8 }}>
            Page not found
          </h1>
          <p style={{ fontSize: 14, color: "#6b7280", marginBottom: 24 }}>
            The page you are looking for does not exist or has been moved.
          </p>
          <Link
            href="/"
            style={{
              display: "inline-block",
              padding: "10px 24px",
              fontSize: 14,
              fontWeight: 500,
              color: "#fff",
              backgroundColor: "#3b82f6",
              border: "none",
              borderRadius: 8,
              textDecoration: "none",
            }}
          >
            Go home
          </Link>
        </div>
      </body>
    </html>
  );
}
