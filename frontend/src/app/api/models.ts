// Types come from the generated OpenAPI schema (npm run generate:api). Do not hand-write DTOs.
import type { components } from './schema';

type Schemas = components['schemas'];

export type LabDto = Schemas['LabDto'];
export type JobDto = Schemas['JobDto'];
export type AuditEventDto = Schemas['AuditEventDto'];
export type EnvironmentDto = Schemas['EnvironmentDto'];
export type CreateLabRequest = Schemas['CreateLabRequest'];
export type CreateLabResponse = Schemas['CreateLabResponse'];
export type LabState = Schemas['LabState'];
export type LabCommand = Schemas['LabCommand'];
export type LabJobStatus = Schemas['LabJobStatus'];
export type LabJobType = Schemas['LabJobType'];
export type VmPowerState = Schemas['VmPowerState'];
export type ProblemDetails = Schemas['HttpValidationProblemDetails'];
