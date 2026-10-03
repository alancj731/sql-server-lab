import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { LabsApi, problemMessage } from '../api/labs-api';
import type { JobDto } from '../api/models';

export interface DeleteLabDialogData {
  labId: string;
  labName: string;
}

/** Deletion requires typing the lab name exactly; the API enforces the same rule. */
@Component({
  selector: 'app-delete-lab-dialog',
  imports: [MatDialogModule, FormsModule],
  template: `
    <h2 mat-dialog-title>Delete lab</h2>
    <mat-dialog-content class="stack">
      <p>
        This deletes the lab's resource group, VM, disks, and SQL Server data. It cannot be undone.
      </p>
      <div class="field">
        <label for="confirm-name"
          >Type <code>{{ data.labName }}</code> to confirm</label
        >
        <input
          id="confirm-name"
          class="input mono"
          name="confirmName"
          autocomplete="off"
          spellcheck="false"
          [ngModel]="typed()"
          (ngModelChange)="typed.set($event)"
          cdkFocusInitial
        />
      </div>
      @if (error()) {
        <div class="alert alert-danger" role="alert">{{ error() }}</div>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button class="btn btn-secondary" type="button" mat-dialog-close>Cancel</button>
      <button
        class="btn btn-dark"
        type="button"
        [disabled]="!matches() || submitting()"
        (click)="confirm()"
      >
        {{ submitting() ? 'Requesting…' : 'Delete lab' }}
      </button>
    </mat-dialog-actions>
  `,
  styles: `
    p {
      margin: 0;
    }
    mat-dialog-content {
      /* room for the input's focus ring inside the scroll container */
      padding-bottom: var(--space-sm);
    }
    .field {
      padding: 0 var(--space-xxs);
    }
    mat-dialog-actions {
      gap: var(--space-sm);
      padding: var(--space-lg) var(--space-xl);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DeleteLabDialog {
  protected readonly data = inject<DeleteLabDialogData>(MAT_DIALOG_DATA);
  private readonly ref = inject<MatDialogRef<DeleteLabDialog, JobDto>>(MatDialogRef);
  private readonly api = inject(LabsApi);

  protected readonly typed = signal('');
  protected readonly matches = computed(() => this.typed() === this.data.labName);
  protected readonly submitting = signal(false);
  protected readonly error = signal<string | null>(null);

  confirm(): void {
    if (!this.matches()) return;
    this.submitting.set(true);
    this.error.set(null);
    this.api.delete(this.data.labId, this.typed()).subscribe({
      next: (job) => this.ref.close(job),
      error: (err) => {
        this.submitting.set(false);
        this.error.set(problemMessage(err));
      },
    });
  }
}
