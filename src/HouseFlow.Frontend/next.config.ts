import type { NextConfig } from "next";
import createNextIntlPlugin from 'next-intl/plugin';

const withNextIntl = createNextIntlPlugin();

const nextConfig: NextConfig = {
  output: 'standalone',
  env: {
    // Aspire injects service URLs via environment variables
    // services__api__https__0 for HTTPS endpoint
    // services__api__http__0 for HTTP endpoint
    NEXT_PUBLIC_API_URL: process.env.services__api__https__0 ||
                         process.env.services__api__http__0 ||
                         'http://localhost:5203',
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
