import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen, fireEvent } from '@testing-library/react';
import { renderWithProviders } from '@/__tests__/test-utils';
import { EditDeviceDialog } from '../edit-device-dialog';

const mockMutate = vi.fn();

vi.mock('@/lib/api/hooks', () => ({
  useUpdateDevice: () => ({
    mutate: mockMutate,
    isPending: false,
    isError: false,
  }),
}));

describe('EditDeviceDialog', () => {
  const defaultProps = {
    deviceId: 'd1',
    device: {
      name: 'Chaudière',
      type: 'Chaudière Gaz',
      brand: 'Viessmann',
      model: 'Vitodens 200',
      installDate: '2023-06-15T00:00:00',
    },
    open: true,
    onClose: vi.fn(),
  };

  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('renders nothing when closed', () => {
    const { container } = renderWithProviders(
      <EditDeviceDialog {...defaultProps} open={false} />
    );
    expect(container.innerHTML).toBe('');
  });

  it('renders form fields with device data', () => {
    renderWithProviders(<EditDeviceDialog {...defaultProps} />);

    expect(screen.getByLabelText('devices.deviceName')).toHaveValue('Chaudière');
    // Radix Select renders the selected value as text in the trigger
    expect(screen.getByRole('combobox')).toHaveTextContent('Chaudière Gaz');
  });

  it('renders brand and model fields', () => {
    renderWithProviders(<EditDeviceDialog {...defaultProps} />);

    const brandInput = screen.getByDisplayValue('Viessmann');
    const modelInput = screen.getByDisplayValue('Vitodens 200');
    expect(brandInput).toBeInTheDocument();
    expect(modelInput).toBeInTheDocument();
  });

  it('updates name on change', () => {
    renderWithProviders(<EditDeviceDialog {...defaultProps} />);

    const nameInput = screen.getByLabelText('devices.deviceName');
    fireEvent.change(nameInput, { target: { value: 'Chaudière Principale' } });
    expect(nameInput).toHaveValue('Chaudière Principale');
  });

  it('calls onClose when cancel is clicked', () => {
    renderWithProviders(<EditDeviceDialog {...defaultProps} />);
    fireEvent.click(screen.getByText('common.cancel'));
    expect(defaultProps.onClose).toHaveBeenCalled();
  });

  it('submits the form with updated data', () => {
    renderWithProviders(<EditDeviceDialog {...defaultProps} />);

    const nameInput = screen.getByLabelText('devices.deviceName');
    fireEvent.change(nameInput, { target: { value: 'Chaudière Principale' } });

    fireEvent.click(screen.getByText('common.save'));

    expect(mockMutate).toHaveBeenCalledWith({
      name: 'Chaudière Principale',
      type: 'Chaudière Gaz',
      brand: 'Viessmann',
      model: 'Vitodens 200',
      installDate: '2023-06-15',
    });
  });
});
