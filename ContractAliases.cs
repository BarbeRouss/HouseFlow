// Type aliases mapping Application DTO names to generated OpenAPI contract types.
// These enable gradual migration from manual DTOs to spec-generated types.
// As the OpenAPI spec grows to cover all endpoints, more aliases will be added here.

global using RegisterRequestDto = HouseFlow.Contracts.RegisterRequest;
global using LoginRequestDto = HouseFlow.Contracts.LoginRequest;
global using CreateHouseRequestDto = HouseFlow.Contracts.CreateHouseRequest;
global using UpdateHouseRequestDto = HouseFlow.Contracts.UpdateHouseRequest;
global using CreateDeviceRequestDto = HouseFlow.Contracts.CreateDeviceRequest;
global using UpdateDeviceRequestDto = HouseFlow.Contracts.UpdateDeviceRequest;
global using LogMaintenanceRequestDto = HouseFlow.Contracts.LogMaintenanceRequest;
global using ConsentRequestDto = HouseFlow.Contracts.ConsentRequest;
global using UpdateProfileRequestDto = HouseFlow.Contracts.UpdateProfileRequest;
global using DeleteAccountRequestDto = HouseFlow.Contracts.DeleteAccountRequest;
global using SetUserAdminRequestDto = HouseFlow.Contracts.SetUserAdminRequest;
global using LastMaintenanceDto = HouseFlow.Contracts.LastMaintenance;
global using LastMaintenanceKind = HouseFlow.Contracts.LastMaintenanceKind;
// Embedded in CreateDeviceRequest.maintenanceType (the hand-written CreateMaintenanceTypeRequestDto
// stays the body of POST /devices/{id}/maintenance-types; both map onto the same service method).
global using DeviceMaintenanceTypeRequestDto = HouseFlow.Contracts.CreateMaintenanceTypeRequest;
