import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen, fireEvent } from '@testing-library/react';
import { renderWithProviders } from '@/__tests__/test-utils';
import { LogMaintenanceDialog } from '../log-maintenance-dialog';

const mockMutate = vi.fn();

vi.mock('@/lib/api/hooks', () => ({
  useLogMaintenance: () => ({
    mutate: mockMutate,
    isPending: false,
    isError: false,
  }),
}));

describe('LogMaintenanceDialog', () => {
  const defaultProps = {
    maintenanceTypeId: 'mt1',
    open: true,
    onClose: vi.fn(),
  };

  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('renders nothing when closed', () => {
    const { container } = renderWithProviders(
      <LogMaintenanceDialog {...defaultProps} open={false} />
    );
    expect(container.innerHTML).toBe('');
  });

  it('renders title and description', () => {
    renderWithProviders(<LogMaintenanceDialog {...defaultProps} />);
    expect(screen.getByText('maintenance.logMaintenance')).toBeInTheDocument();
    expect(screen.getByText('maintenance.logDescription')).toBeInTheDocument();
  });

  it('shows quick and detailed mode buttons', () => {
    renderWithProviders(<LogMaintenanceDialog {...defaultProps} />);
    expect(screen.getByText('maintenance.quickLog')).toBeInTheDocument();
    expect(screen.getByText('maintenance.detailedLog')).toBeInTheDocument();
  });

  it('shows date field by default (quick mode)', () => {
    renderWithProviders(<LogMaintenanceDialog {...defaultProps} />);
    expect(screen.getByLabelText('maintenance.date')).toBeInTheDocument();
    expect(screen.queryByLabelText('maintenance.provider')).not.toBeInTheDocument();
  });

  it('shows detailed fields when switching to detailed mode', () => {
    renderWithProviders(<LogMaintenanceDialog {...defaultProps} />);

    fireEvent.click(screen.getByText('maintenance.detailedLog'));

    expect(screen.getByLabelText('maintenance.provider')).toBeInTheDocument();
    expect(screen.getByLabelText('maintenance.notes')).toBeInTheDocument();
  });

  it('calls onClose when cancel is clicked', () => {
    renderWithProviders(<LogMaintenanceDialog {...defaultProps} />);
    fireEvent.click(screen.getByText('common.cancel'));
    expect(defaultProps.onClose).toHaveBeenCalled();
  });

  it('submits quick log with date only', () => {
    renderWithProviders(<LogMaintenanceDialog {...defaultProps} />);

    fireEvent.change(screen.getByLabelText('maintenance.date'), {
      target: { value: '2024-06-15' },
    });

    fireEvent.click(screen.getByText('common.save'));

    expect(mockMutate).toHaveBeenCalledWith(
      expect.objectContaining({
        cost: null,
        provider: null,
        notes: null,
      })
    );
  });

  it('submits detailed log with all fields', () => {
    renderWithProviders(<LogMaintenanceDialog {...defaultProps} />);

    fireEvent.click(screen.getByText('maintenance.detailedLog'));

    fireEvent.change(screen.getByLabelText('maintenance.date'), {
      target: { value: '2024-06-15' },
    });
    fireEvent.change(screen.getByLabelText(/cost/i), {
      target: { value: '150' },
    });
    fireEvent.change(screen.getByLabelText('maintenance.provider'), {
      target: { value: 'Technicien Pro' },
    });
    fireEvent.change(screen.getByLabelText('maintenance.notes'), {
      target: { value: 'RAS' },
    });

    fireEvent.click(screen.getByText('common.save'));

    expect(mockMutate).toHaveBeenCalledWith(
      expect.objectContaining({
        cost: 150,
        provider: 'Technicien Pro',
        notes: 'RAS',
      })
    );
  });
});
