import { HttpClient, HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import type {
  AuditEventDto,
  CreateLabRequest,
  CreateLabResponse,
  EnvironmentDto,
  JobDto,
  LabDto,
  ProblemDetails,
} from './models';

const BASE = '/api/v1';

/** Typed client for /api/v1. Every mutation sends a fresh Idempotency-Key so double submits are safe. */
@Injectable({ providedIn: 'root' })
export class LabsApi {
  private readonly http = inject(HttpClient);

  environment(): Observable<EnvironmentDto> {
    return this.http.get<EnvironmentDto>(`${BASE}/environment`);
  }

  list(): Observable<LabDto[]> {
    return this.http.get<LabDto[]>(`${BASE}/labs`);
  }

  get(labId: string): Observable<LabDto> {
    return this.http.get<LabDto>(`${BASE}/labs/${labId}`);
  }

  create(request: CreateLabRequest): Observable<CreateLabResponse> {
    return this.http.post<CreateLabResponse>(`${BASE}/labs`, request, { headers: idempotent() });
  }

  start(labId: string): Observable<JobDto> {
    return this.http.post<JobDto>(`${BASE}/labs/${labId}/start`, null, { headers: idempotent() });
  }

  deallocate(labId: string): Observable<JobDto> {
    return this.http.post<JobDto>(`${BASE}/labs/${labId}/deallocate`, null, {
      headers: idempotent(),
    });
  }

  extendExpiration(labId: string, hours: number): Observable<LabDto> {
    return this.http.post<LabDto>(`${BASE}/labs/${labId}/extend-expiration`, { hours });
  }

  delete(labId: string, confirmName: string): Observable<JobDto> {
    return this.http.request<JobDto>('DELETE', `${BASE}/labs/${labId}`, {
      body: { confirmName },
      headers: idempotent(),
    });
  }

  jobs(labId: string): Observable<JobDto[]> {
    return this.http.get<JobDto[]>(`${BASE}/labs/${labId}/jobs`);
  }

  audit(labId: string): Observable<AuditEventDto[]> {
    return this.http.get<AuditEventDto[]>(`${BASE}/labs/${labId}/audit`);
  }

  job(jobId: string): Observable<JobDto> {
    return this.http.get<JobDto>(`${BASE}/jobs/${jobId}`);
  }

  cancelJob(jobId: string): Observable<JobDto> {
    return this.http.post<JobDto>(`${BASE}/jobs/${jobId}/cancel`, null);
  }
}

function idempotent(): HttpHeaders {
  return new HttpHeaders({ 'Idempotency-Key': crypto.randomUUID() });
}

/** Human-readable message from an RFC 9457 problem response. */
export function problemMessage(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    const problem = error.error as Partial<ProblemDetails> | null;
    if (problem && typeof problem === 'object') {
      if (problem.errors) {
        const first = Object.values(problem.errors)[0];
        if (first?.length) {
          return first[0];
        }
      }

      if (problem.detail) {
        return problem.detail;
      }

      if (problem.title) {
        return problem.title;
      }
    }

    if (error.status === 0) {
      return 'The API is unreachable. Check that it is running.';
    }

    if (error.status === 429) {
      return 'Too many requests. Wait a moment and try again.';
    }

    return `Request failed (${error.status}).`;
  }

  return 'Unexpected error.';
}

/** Field errors keyed by lower-camel field name. */
export function problemFieldErrors(error: unknown): Record<string, string> {
  const result: Record<string, string> = {};
  if (error instanceof HttpErrorResponse && error.status === 400) {
    const errors = (error.error as Partial<ProblemDetails> | null)?.errors ?? {};
    for (const [key, messages] of Object.entries(errors)) {
      if (messages?.length) {
        result[key.charAt(0).toLowerCase() + key.slice(1)] = messages[0];
      }
    }
  }

  return result;
}
