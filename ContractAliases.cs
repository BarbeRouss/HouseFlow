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
