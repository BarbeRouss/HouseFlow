import type { NextConfig } from "next";
import createNextIntlPlugin from 'next-intl/plugin';

const withNextIntl = createNextIntlPlugin();

const nextConfig: NextConfig = {
  output: 'standalone',
  env: {
    // Aspire injects service URLs via environment variables (dev only)
    // In production, API_URL is read at runtime in layout.tsx
    ...(process.env.services__api__https__0 || process.env.services__api__http__0
      ? {
          NEXT_PUBLIC_API_URL:
            process.env.services__api__https__0 || process.env.services__api__http__0,
        }
      : {}),
  },
  images: {
    remotePatterns: [
      {
        protocol: 'http',
        hostname: 'localhost',
      },
      {
        protocol: 'https',
        hostname: 'localhost',
      },
      {
        protocol: 'https',
        hostname: 'api.houseflow.rouss.be',
      },
    ],
  },
  reactStrictMode: true,
  experimental: {
    // Enable Server Actions for Next.js 15
    serverActions: {
      bodySizeLimit: '2mb',
    },
  },
};

export default withNextIntl(nextConfig);
