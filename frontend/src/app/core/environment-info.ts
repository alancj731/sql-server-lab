import { Injectable, inject, signal } from '@angular/core';
import { LabsApi } from '../api/labs-api';
import type { EnvironmentDto } from '../api/models';

@Injectable({ providedIn: 'root' })
export class EnvironmentInfo {
  private readonly api = inject(LabsApi);
  readonly value = signal<EnvironmentDto | null>(null);

  load(): void {
    this.api.environment().subscribe({
      next: (env) => this.value.set(env),
      error: () => this.value.set(null),
    });
  }
}
