import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen, fireEvent } from '@testing-library/react';
import { renderWithProviders } from '@/__tests__/test-utils';
import { DeleteHouseDialog } from '../delete-house-dialog';

let mockMutate: ReturnType<typeof vi.fn>;

vi.mock('@/lib/api/hooks', () => ({
  useDeleteHouse: (options?: { onSuccess?: () => void }) => {
    mockMutate = vi.fn((id) => {
      options?.onSuccess?.();
    });
    return {
      mutate: mockMutate,
      isPending: false,
      isError: false,
    };
  },
}));

describe('DeleteHouseDialog', () => {
  const defaultProps = {
    houseId: 'h1',
    houseName: 'Maison Test',
    open: true,
    onClose: vi.fn(),
  };

  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('renders nothing when closed', () => {
    const { container } = renderWithProviders(
      <DeleteHouseDialog {...defaultProps} open={false} />
    );
    expect(container.innerHTML).toBe('');
  });

  it('shows confirmation message', () => {
    renderWithProviders(<DeleteHouseDialog {...defaultProps} />);
    expect(screen.getByText('houses.deleteConfirmation')).toBeInTheDocument();
  });

  it('shows the delete title', () => {
    renderWithProviders(<DeleteHouseDialog {...defaultProps} />);
    expect(screen.getByText('houses.deleteHouse')).toBeInTheDocument();
  });

  it('calls onClose when cancel is clicked', () => {
    renderWithProviders(<DeleteHouseDialog {...defaultProps} />);
    fireEvent.click(screen.getByText('common.cancel'));
    expect(defaultProps.onClose).toHaveBeenCalled();
  });

  it('calls delete mutation when delete is clicked', () => {
    renderWithProviders(<DeleteHouseDialog {...defaultProps} />);
    fireEvent.click(screen.getByText('common.delete'));
    expect(mockMutate).toHaveBeenCalledWith('h1');
  });
});
