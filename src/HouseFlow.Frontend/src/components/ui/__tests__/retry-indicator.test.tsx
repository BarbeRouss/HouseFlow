import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, act } from '@testing-library/react';
import '@testing-library/jest-dom';

// Track the listener that the component registers
let registeredListener: ((retrying: boolean) => void) | null = null;

vi.mock('@/lib/api/hooks/use-retry-state', () => {
  const { useState, useEffect } = require('react');

  return {
    useRetryState: () => {
      const [isRetrying, setIsRetrying] = useState(false);
      useEffect(() => {
        registeredListener = setIsRetrying;
        return () => { registeredListener = null; };
      }, []);
      return isRetrying;
    },
  };
});

vi.mock('next-intl', () => ({
  useTranslations: () => (key: string) => {
    const translations: Record<string, string> = {
      retrying: 'Reconnexion en cours…',
    };
    return translations[key] || key;
  },
}));

import { RetryIndicator } from '../retry-indicator';

describe('RetryIndicator', () => {
  beforeEach(() => {
    registeredListener = null;
  });

  it('renders with role="status" for accessibility', () => {
    render(<RetryIndicator />);
    expect(screen.getByRole('status')).toBeInTheDocument();
  });

  it('is hidden when not retrying', () => {
    render(<RetryIndicator />);
    const indicator = screen.getByRole('status');
    expect(indicator).toHaveClass('opacity-0');
    expect(indicator).toHaveClass('pointer-events-none');
  });

  it('becomes visible when retrying', () => {
    render(<RetryIndicator />);

    act(() => {
      registeredListener?.(true);
    });

    const indicator = screen.getByRole('status');
    expect(indicator).toHaveClass('opacity-100');
    expect(indicator).not.toHaveClass('pointer-events-none');
  });

  it('displays the retrying text', () => {
    render(<RetryIndicator />);

    act(() => {
      registeredListener?.(true);
    });

    expect(screen.getByText('Reconnexion en cours…')).toBeInTheDocument();
  });

  it('hides when retry completes', () => {
    render(<RetryIndicator />);

    act(() => {
      registeredListener?.(true);
    });

    act(() => {
      registeredListener?.(false);
    });

    const indicator = screen.getByRole('status');
    expect(indicator).toHaveClass('opacity-0');
  });
});
