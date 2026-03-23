import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen, fireEvent } from '@testing-library/react';
import { renderWithProviders } from '@/__tests__/test-utils';
import { DeleteDeviceDialog } from '../delete-device-dialog';

let mockMutate: ReturnType<typeof vi.fn>;

vi.mock('@/lib/api/hooks', () => ({
  useDeleteDevice: (options?: { onSuccess?: () => void }) => {
    mockMutate = vi.fn((params) => {
      options?.onSuccess?.();
    });
    return {
      mutate: mockMutate,
      isPending: false,
      isError: false,
    };
  },
}));

describe('DeleteDeviceDialog', () => {
  const defaultProps = {
    deviceId: 'd1',
    deviceName: 'Chaudière',
    houseId: 'h1',
    open: true,
    onClose: vi.fn(),
  };

  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('renders nothing when closed', () => {
    const { container } = renderWithProviders(
      <DeleteDeviceDialog {...defaultProps} open={false} />
    );
    expect(container.innerHTML).toBe('');
  });

  it('shows confirmation message', () => {
    renderWithProviders(<DeleteDeviceDialog {...defaultProps} />);
    expect(screen.getByText('devices.deleteConfirmation')).toBeInTheDocument();
  });

  it('shows the delete title', () => {
    renderWithProviders(<DeleteDeviceDialog {...defaultProps} />);
    expect(screen.getByText('devices.deleteDevice')).toBeInTheDocument();
  });

  it('calls onClose when cancel is clicked', () => {
    renderWithProviders(<DeleteDeviceDialog {...defaultProps} />);
    fireEvent.click(screen.getByText('common.cancel'));
    expect(defaultProps.onClose).toHaveBeenCalled();
  });

  it('calls delete mutation with correct params', () => {
    renderWithProviders(<DeleteDeviceDialog {...defaultProps} />);
    fireEvent.click(screen.getByText('common.delete'));
    expect(mockMutate).toHaveBeenCalledWith({ deviceId: 'd1', houseId: 'h1' });
  });
});
