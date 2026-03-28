import { cn } from "@/lib/utils";

interface LogoProps {
  className?: string;
  size?: number;
}

export function Logo({ className, size = 24 }: LogoProps) {
  return (
    <svg
      xmlns="http://www.w3.org/2000/svg"
      viewBox="0 0 120 120"
      width={size}
      height={size}
      className={cn(className)}
      aria-hidden="true"
    >
      <defs>
        <linearGradient id="hf-grad" x1="0%" y1="0%" x2="100%" y2="100%">
          <stop offset="0%" stopColor="#6366f1" />
          <stop offset="100%" stopColor="#818cf8" />
        </linearGradient>
      </defs>
      <rect x="4" y="4" width="112" height="112" rx="26" fill="url(#hf-grad)" />
      <line x1="34" y1="98" x2="34" y2="36" stroke="white" strokeWidth="7" strokeLinecap="round" />
      <line x1="86" y1="98" x2="86" y2="36" stroke="white" strokeWidth="7" strokeLinecap="round" />
      <path d="M26 40 L60 14 L94 40" stroke="white" strokeWidth="7" fill="none" strokeLinecap="round" strokeLinejoin="round" />
      <path d="M34 64 C46 48, 54 80, 60 64 C66 48, 74 80, 86 64" stroke="white" strokeWidth="5.5" fill="none" strokeLinecap="round" />
    </svg>
  );
}
