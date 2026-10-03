import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { LabsApi, problemFieldErrors, problemMessage } from '../api/labs-api';
import { EnvironmentInfo } from '../core/environment-info';

export const LAB_NAME_PATTERN = /^[a-z][a-z0-9-]{1,28}[a-z0-9]$/;

@Component({
  selector: 'app-new-lab',
  imports: [ReactiveFormsModule, RouterLink],
  template: `
    <nav class="caption crumbs" aria-label="Breadcrumb">
      <a routerLink="/labs">Labs</a> / New lab
    </nav>
    <div class="layout">
      <form class="card stack" [formGroup]="form" (ngSubmit)="submit()" novalidate>
        <div>
          <h1 class="display-md">Create a lab</h1>
          <p class="mute">
            Provisions an Ubuntu 22.04 VM with SQL Server 2022 Developer Edition in a private
            subnet. No public IP, no SSH.
          </p>
        </div>

        <div class="field">
          <label for="name">Lab name</label>
          <input
            id="name"
            class="input mono"
            formControlName="name"
            autocomplete="off"
            spellcheck="false"
            [attr.aria-invalid]="showError('name')"
            aria-describedby="name-hint name-error"
          />
          <span id="name-hint" class="field-hint"
            >3–30 lowercase letters, digits, or hyphens. Starts with a letter.</span
          >
          @if (showError('name')) {
            <span id="name-error" class="field-error">{{ errorFor('name') }}</span>
          }
        </div>

        <div class="field">
          <label for="region">Region</label>
          <select
            id="region"
            class="select"
            formControlName="region"
            [attr.aria-invalid]="showError('region')"
          >
            @for (region of regions(); track region) {
              <option [value]="region">{{ region }}</option>
            }
          </select>
          @if (showError('region')) {
            <span class="field-error">{{ errorFor('region') }}</span>
          }
        </div>

        <div class="field">
          <label for="ttl">Time to live</label>
          <select id="ttl" class="select" formControlName="ttlHours" aria-describedby="ttl-hint">
            @for (h of ttlOptions(); track h) {
              <option [ngValue]="h">{{ h }} {{ h === 1 ? 'hour' : 'hours' }}</option>
            }
          </select>
          <span id="ttl-hint" class="field-hint"
            >The lab is deleted automatically when it expires. You can extend it later.</span
          >
          @if (showError('ttlHours')) {
            <span class="field-error">{{ errorFor('ttlHours') }}</span>
          }
        </div>

        @if (error()) {
          <div class="alert alert-danger" role="alert">{{ error() }}</div>
        }

        <div class="row">
          <button class="btn btn-primary" type="submit" [disabled]="submitting()">
            {{ submitting() ? 'Requesting…' : 'Create lab' }}
          </button>
          <a routerLink="/labs" class="btn btn-secondary">Cancel</a>
        </div>
      </form>

      <aside class="card notice stack" aria-label="Cost and limits">
        <h2 class="heading-md">Before you start</h2>
        @if (environment.value()?.isSimulated) {
          <p class="caption">
            <strong>Simulated environment.</strong> Provisioning takes seconds and creates no Azure
            resources.
          </p>
        } @else {
          <p class="caption">
            A running lab VM costs about $0.10/hour (Standard_D2s_v7; SQL Server Developer Edition
            is free). Its disk is billed until the lab is deleted. Deallocate it when idle and
            delete it when done.
          </p>
        }
        <ul class="caption">
          <li>Up to {{ environment.value()?.maxActiveLabsPerUser ?? 2 }} labs per user.</li>
          <li>Maximum lifetime 24 hours.</li>
          <li>Only allow-listed experiments run; arbitrary SQL is never accepted.</li>
        </ul>
      </aside>
    </div>
  `,
  styles: `
    .crumbs {
      margin-bottom: var(--space-lg);
    }
    .layout {
      display: grid;
      gap: var(--space-xl);
      grid-template-columns: minmax(0, 1fr);
    }
    @media (min-width: 1024px) {
      .layout {
        grid-template-columns: minmax(0, 2fr) minmax(0, 1fr);
        align-items: start;
      }
    }
    form p {
      margin: var(--space-xs) 0 0;
    }
    .notice {
      background: var(--color-canvas-soft);
    }
    .notice p,
    .notice ul {
      margin: 0;
    }
    .notice ul {
      padding-left: 18px;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class NewLab {
  private readonly api = inject(LabsApi);
  private readonly router = inject(Router);
  protected readonly environment = inject(EnvironmentInfo);

  protected readonly form = inject(NonNullableFormBuilder).group({
    name: ['', [Validators.required, Validators.pattern(LAB_NAME_PATTERN)]],
    region: ['centralus', Validators.required],
    ttlHours: [4, [Validators.required, Validators.min(1), Validators.max(8)]],
  });

  protected readonly submitting = signal(false);
  protected readonly submitted = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly serverErrors = signal<Record<string, string>>({});

  protected readonly regions = computed(() => this.environment.value()?.regions ?? ['centralus']);

  constructor() {
    // Default to the first region this deployment offers (Azure mode offers only the platform region).
    effect(() => {
      const regions = this.regions();
      const control = this.form.controls.region;
      if (regions.length > 0 && !regions.includes(control.value)) {
        control.setValue(regions[0]);
      }
    });
  }
  protected readonly ttlOptions = computed(() => {
    const env = this.environment.value();
    const min = env?.minTtlHours ?? 1;
    const max = env?.maxTtlHours ?? 8;
    return Array.from({ length: max - min + 1 }, (_, i) => min + i);
  });

  protected showError(field: 'name' | 'region' | 'ttlHours'): boolean {
    const control = this.form.controls[field];
    return (
      !!this.serverErrors()[field] || (control.invalid && (control.touched || this.submitted()))
    );
  }

  protected errorFor(field: 'name' | 'region' | 'ttlHours'): string {
    const server = this.serverErrors()[field];
    if (server) return server;
    const control = this.form.controls[field];
    if (control.hasError('required')) return 'Required.';
    if (control.hasError('pattern'))
      return 'Use 3–30 lowercase letters, digits, or hyphens; start with a letter.';
    return 'Invalid value.';
  }

  submit(): void {
    this.submitted.set(true);
    this.serverErrors.set({});
    this.error.set(null);
    if (this.form.invalid) {
      return;
    }

    this.submitting.set(true);
    this.api.create(this.form.getRawValue()).subscribe({
      next: (result) => void this.router.navigate(['/labs', result.lab.id, 'overview']),
      error: (err) => {
        this.submitting.set(false);
        const fields = problemFieldErrors(err);
        this.serverErrors.set(fields);
        if (Object.keys(fields).length === 0) {
          this.error.set(problemMessage(err));
        }
      },
    });
  }
}
