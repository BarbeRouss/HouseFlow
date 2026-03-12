"use client";

interface ScoreRingProps {
  score: number;
  size?: 'sm' | 'md' | 'lg';
  showLabel?: boolean;
}

export function ScoreRing({ score, size = 'md', showLabel = true }: ScoreRingProps) {
  // Size configurations
  const sizeConfig = {
    sm: { width: 48, strokeWidth: 3, radius: 18, fontSize: 'text-xs' },
    md: { width: 80, strokeWidth: 4, radius: 32, fontSize: 'text-xl' },
    lg: { width: 120, strokeWidth: 6, radius: 48, fontSize: 'text-3xl' },
  };

  const config = sizeConfig[size];
  const circumference = 2 * Math.PI * config.radius;
  const strokeDashoffset = circumference - (score / 100) * circumference;

  // Color based on score
  const getColor = () => {
    if (score >= 80) return { stroke: '#22c55e', bg: 'text-green-600' };
    if (score >= 50) return { stroke: '#f97316', bg: 'text-orange-600' };
    return { stroke: '#ef4444', bg: 'text-red-600' };
  };

  const colors = getColor();

  return (
    <div className="relative flex-shrink-0" style={{ width: config.width, height: config.width }}>
      <svg
        className="transform -rotate-90"
        width={config.width}
        height={config.width}
        viewBox={`0 0 ${config.width} ${config.width}`}
      >
        {/* Background circle */}
        <circle
          cx={config.width / 2}
          cy={config.width / 2}
          r={config.radius}
          fill="none"
          stroke="#e5e7eb"
          strokeWidth={config.strokeWidth}
        />
        {/* Progress circle */}
        <circle
          cx={config.width / 2}
          cy={config.width / 2}
          r={config.radius}
          fill="none"
          stroke={colors.stroke}
          strokeWidth={config.strokeWidth}
          strokeLinecap="round"
          strokeDasharray={circumference}
          strokeDashoffset={strokeDashoffset}
          className="transition-all duration-500 ease-out"
        />
      </svg>
      {showLabel && (
        <div className="absolute inset-0 flex items-center justify-center">
          <span className={`font-bold ${colors.bg} ${config.fontSize}`}>
            {score}%
          </span>
        </div>
      )}
    </div>
  );
}
