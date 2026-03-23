import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen, fireEvent, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { renderWithProviders } from '@/__tests__/test-utils';
import { AddMaintenanceTypeDialog } from '../add-maintenance-type-dialog';

const mockMutate = vi.fn();

vi.mock('@/lib/api/hooks', () => ({
  useCreateMaintenanceType: () => ({
    mutate: mockMutate,
    isPending: false,
    isError: false,
  }),
}));

describe('AddMaintenanceTypeDialog', () => {
  const defaultProps = {
    deviceId: 'd1',
    open: true,
    onClose: vi.fn(),
  };

  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('renders nothing when closed', () => {
    const { container } = renderWithProviders(
      <AddMaintenanceTypeDialog {...defaultProps} open={false} />
    );
    expect(container.innerHTML).toBe('');
  });

  it('renders form with name and periodicity fields', () => {
    renderWithProviders(<AddMaintenanceTypeDialog {...defaultProps} />);

    expect(screen.getByLabelText('maintenance.typeName')).toBeInTheDocument();
    // Radix Select renders a combobox trigger instead of a native select
    expect(screen.getByRole('combobox')).toBeInTheDocument();
  });

  it('shows title and description', () => {
    renderWithProviders(<AddMaintenanceTypeDialog {...defaultProps} />);
    expect(screen.getByText('maintenance.addMaintenanceType')).toBeInTheDocument();
    expect(screen.getByText('maintenance.addMaintenanceTypeDescription')).toBeInTheDocument();
  });

  it('shows custom days field when Custom periodicity is selected', async () => {
    const user = userEvent.setup();
    renderWithProviders(<AddMaintenanceTypeDialog {...defaultProps} />);

    expect(screen.queryByLabelText('maintenance.customDays')).not.toBeInTheDocument();

    // Open the Radix Select and pick Custom
    await user.click(screen.getByRole('combobox'));
    await user.click(screen.getByRole('option', { name: 'maintenance.custom' }));

    expect(screen.getByLabelText('maintenance.customDays')).toBeInTheDocument();
  });

  it('calls onClose when cancel is clicked', () => {
    renderWithProviders(<AddMaintenanceTypeDialog {...defaultProps} />);
    fireEvent.click(screen.getByText('common.cancel'));
    expect(defaultProps.onClose).toHaveBeenCalled();
  });

  it('submits with correct data', () => {
    renderWithProviders(<AddMaintenanceTypeDialog {...defaultProps} />);

    fireEvent.change(screen.getByLabelText('maintenance.typeName'), {
      target: { value: 'Révision annuelle' },
    });

    fireEvent.click(screen.getByText('common.add'));

    expect(mockMutate).toHaveBeenCalledWith({
      name: 'Révision annuelle',
      periodicity: 'Annual',
      customDays: null,
    });
  });

  it('submits with custom days when Custom is selected', async () => {
    const user = userEvent.setup();
    renderWithProviders(<AddMaintenanceTypeDialog {...defaultProps} />);

    fireEvent.change(screen.getByLabelText('maintenance.typeName'), {
      target: { value: 'Custom check' },
    });

    // Open the Radix Select and pick Custom
    await user.click(screen.getByRole('combobox'));
    await user.click(screen.getByRole('option', { name: 'maintenance.custom' }));

    fireEvent.change(screen.getByLabelText('maintenance.customDays'), {
      target: { value: '90' },
    });

    fireEvent.click(screen.getByText('common.add'));

    expect(mockMutate).toHaveBeenCalledWith({
      name: 'Custom check',
      periodicity: 'Custom',
      customDays: 90,
    });
  });

  it('closes backdrop on click', () => {
    renderWithProviders(<AddMaintenanceTypeDialog {...defaultProps} />);

    const backdrop = screen.getByText('maintenance.addMaintenanceType').closest('.fixed');
    fireEvent.click(backdrop!);
    expect(defaultProps.onClose).toHaveBeenCalled();
  });
});
