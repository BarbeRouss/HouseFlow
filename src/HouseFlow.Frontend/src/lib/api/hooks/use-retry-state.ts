import { useEffect, useState } from 'react';
import { onRetryStateChange } from '../client';

/**
 * Hook that tracks whether any API request is currently being retried.
 * Connects to the Axios retry interceptor via the onRetryStateChange listener.
 */
export function useRetryState(): boolean {
  const [isRetrying, setIsRetrying] = useState(false);

  useEffect(() => {
    return onRetryStateChange(setIsRetrying);
  }, []);

  return isRetrying;
}
