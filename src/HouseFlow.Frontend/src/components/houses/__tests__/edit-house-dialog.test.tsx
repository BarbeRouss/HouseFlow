import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen, fireEvent, waitFor } from '@testing-library/react';
import { renderWithProviders } from '@/__tests__/test-utils';
import { EditHouseDialog } from '../edit-house-dialog';

vi.mock('@/lib/api/hooks', () => ({
  useUpdateHouse: (houseId: string, options?: { onSuccess?: () => void }) => {
    const mutate = vi.fn((data) => {
      mockMutate(data);
      if (!mockShouldError) {
        options?.onSuccess?.();
      }
    });
    return {
      mutate,
      isPending: false,
      isError: mockShouldError,
    };
  },
}));

let mockMutate: ReturnType<typeof vi.fn>;
let mockShouldError: boolean;

describe('EditHouseDialog', () => {
  const defaultProps = {
    houseId: 'h1',
    house: { name: 'Maison', address: '123 Rue', zipCode: '75001', city: 'Paris' },
    open: true,
    onClose: vi.fn(),
  };

  beforeEach(() => {
    vi.clearAllMocks();
    mockMutate = vi.fn();
    mockShouldError = false;
  });

  it('renders nothing when closed', () => {
    const { container } = renderWithProviders(
      <EditHouseDialog {...defaultProps} open={false} />
    );
    expect(container.innerHTML).toBe('');
  });

  it('renders form fields with house data when open', () => {
    renderWithProviders(<EditHouseDialog {...defaultProps} />);

    expect(screen.getByLabelText('houses.houseName')).toHaveValue('Maison');
    expect(screen.getByLabelText('houses.address')).toHaveValue('123 Rue');
    expect(screen.getByLabelText('houses.zipCode')).toHaveValue('75001');
    expect(screen.getByLabelText('houses.city')).toHaveValue('Paris');
  });

  it('updates input values on change', () => {
    renderWithProviders(<EditHouseDialog {...defaultProps} />);

    const nameInput = screen.getByLabelText('houses.houseName');
    fireEvent.change(nameInput, { target: { value: 'Maison Bleue' } });
    expect(nameInput).toHaveValue('Maison Bleue');
  });

  it('calls onClose when cancel is clicked', () => {
    renderWithProviders(<EditHouseDialog {...defaultProps} />);

    fireEvent.click(screen.getByText('common.cancel'));
    expect(defaultProps.onClose).toHaveBeenCalled();
  });

  it('submits the form with updated data', () => {
    renderWithProviders(<EditHouseDialog {...defaultProps} />);

    const nameInput = screen.getByLabelText('houses.houseName');
    fireEvent.change(nameInput, { target: { value: 'Maison Bleue' } });

    fireEvent.submit(screen.getByText('common.save').closest('form')!);

    expect(mockMutate).toHaveBeenCalledWith({
      name: 'Maison Bleue',
      address: '123 Rue',
      zipCode: '75001',
      city: 'Paris',
    });
  });
});
