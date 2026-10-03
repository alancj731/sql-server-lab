import { ChangeDetectionStrategy, Component, OnInit, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterOutlet } from '@angular/router';
import { filter, map } from 'rxjs';
import { AuthService } from './core/auth';
import { Clock } from './core/clock';
import { EnvironmentInfo } from './core/environment-info';
import { LabEvents } from './core/lab-events';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink],
  template: `
    <a class="skip-link" href="#main">Skip to content</a>
    <header class="nav">
      <div class="container nav-inner">
        <a routerLink="/labs" class="brand" aria-label="SQL Server Lab home">
          <span class="brand-mark" aria-hidden="true"></span>
          <span>SQL Server <span class="brand-accent">Lab</span></span>
        </a>
        <div class="row">
          @if (auth.enabled) {
            <span class="caption user">{{ auth.account()?.name ?? auth.account()?.username }}</span>
            <button type="button" class="btn btn-link caption" (click)="auth.signOut()">
              Sign out
            </button>
          }
          <span class="caption conn" [attr.data-status]="events.status()">
            <span class="dot" aria-hidden="true"></span>
            <span>Live updates: {{ events.status() }}</span>
          </span>
          <!-- One emerald CTA per viewport: the create form has its own primary button. -->
          @if (!onNewLabPage()) {
            <a routerLink="/labs/new" class="btn btn-primary">New lab</a>
          }
        </div>
      </div>
    </header>

    @if (environment.value()?.isSimulated) {
      <div class="sim-band" role="note">
        <div class="container row">
          <span class="pill"><span class="dot sim-dot" aria-hidden="true"></span>Simulated</span>
          <span class="caption">
            Local mode: lab operations are simulated and no Azure resources are created or billed.
          </span>
        </div>
      </div>
    }

    <main id="main" class="container" tabindex="-1">
      <router-outlet />
    </main>

    <footer class="footer">
      <div class="container caption">
        Educational lab — not a production SQL Server administration portal. Labs expire
        automatically.
      </div>
    </footer>
  `,
  styles: `
    :host {
      display: flex;
      flex-direction: column;
      min-height: 100vh;
    }
    .skip-link {
      position: absolute;
      left: -999px;
      top: 8px;
      z-index: 10;
      background: var(--color-canvas);
      padding: 8px 12px;
      border-radius: var(--radius-sm);
    }
    .skip-link:focus {
      left: 8px;
    }
    .nav {
      border-bottom: 1px solid var(--color-hairline-cool);
      background: var(--color-canvas);
    }
    .nav-inner {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: var(--space-md);
      padding-top: var(--space-lg);
      padding-bottom: var(--space-lg);
    }
    .brand {
      display: inline-flex;
      align-items: center;
      gap: 10px;
      font-weight: 500;
      font-size: 17px;
      letter-spacing: -0.2px;
      text-decoration: none;
    }
    .brand-mark {
      width: 18px;
      height: 18px;
      border-radius: 5px;
      background: var(--color-canvas-night);
      box-shadow: inset 0 -6px 0 var(--color-primary);
    }
    .brand-accent {
      color: var(--color-primary-deep);
    }
    .user {
      display: none;
    }
    @media (min-width: 768px) {
      .user {
        display: inline;
      }
    }
    .conn {
      display: none;
      align-items: center;
      gap: 6px;
    }
    .conn[data-status='connected'] .dot {
      background: var(--color-primary);
    }
    .conn[data-status='reconnecting'] .dot,
    .conn[data-status='connecting'] .dot {
      background: var(--color-accent-yellow);
    }
    @media (min-width: 768px) {
      .conn {
        display: inline-flex;
      }
    }
    .sim-band {
      background: var(--color-canvas-soft);
      border-bottom: 1px solid var(--color-hairline-cool);
      padding: var(--space-sm) 0;
    }
    .sim-dot {
      background: var(--color-accent-yellow);
    }
    main {
      flex: 1;
      padding-top: var(--space-xxl);
      padding-bottom: var(--space-huge);
      outline: none;
    }
    .footer {
      border-top: 1px solid var(--color-hairline-cool);
      padding: var(--space-xl) 0;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class App implements OnInit {
  protected readonly environment = inject(EnvironmentInfo);
  protected readonly events = inject(LabEvents);
  protected readonly auth = inject(AuthService);
  private readonly clock = inject(Clock);
  private readonly router = inject(Router);
  protected readonly onNewLabPage = toSignal(
    this.router.events.pipe(
      filter((e): e is NavigationEnd => e instanceof NavigationEnd),
      map((e) => e.urlAfterRedirects.startsWith('/labs/new')),
    ),
    { initialValue: false },
  );

  ngOnInit(): void {
    this.environment.load();
    this.events.start();
    void this.clock;
  }
}
