import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { LabsApi, problemFieldErrors, problemMessage } from './labs-api';

describe('LabsApi', () => {
  let api: LabsApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    api = TestBed.inject(LabsApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('sends a fresh idempotency key with every mutation', () => {
    api.create({ name: 'lab-a', region: 'eastus', ttlHours: 2 }).subscribe();
    api.create({ name: 'lab-a', region: 'eastus', ttlHours: 2 }).subscribe();
    const [first, second] = http.match('/api/v1/labs');
    const k1 = first.request.headers.get('Idempotency-Key');
    const k2 = second.request.headers.get('Idempotency-Key');
    expect(k1).toBeTruthy();
    expect(k1).not.toEqual(k2);
    expect(first.request.body).toEqual({ name: 'lab-a', region: 'eastus', ttlHours: 2 });
    first.flush({});
    second.flush({});
  });

  it('sends the typed confirmation name in the delete body', () => {
    api.delete('id-1', 'lab-a').subscribe();
    const req = http.expectOne('/api/v1/labs/id-1');
    expect(req.request.method).toBe('DELETE');
    expect(req.request.body).toEqual({ confirmName: 'lab-a' });
    req.flush({});
  });

  it('targets allow-listed command endpoints only', () => {
    api.start('x').subscribe();
    api.deallocate('x').subscribe();
    api.cancelJob('j').subscribe();
    http.expectOne({ method: 'POST', url: '/api/v1/labs/x/start' }).flush({});
    http.expectOne({ method: 'POST', url: '/api/v1/labs/x/deallocate' }).flush({});
    http.expectOne({ method: 'POST', url: '/api/v1/jobs/j/cancel' }).flush({});
  });
});

describe('problem details parsing', () => {
  const error = (status: number, body: unknown) => new HttpErrorResponse({ status, error: body });

  it('prefers the first field error, then detail, then title', () => {
    expect(problemMessage(error(400, { errors: { Name: ['Bad name'] }, detail: 'x' }))).toBe(
      'Bad name',
    );
    expect(problemMessage(error(409, { title: 'Conflict', detail: 'Busy' }))).toBe('Busy');
    expect(problemMessage(error(409, { title: 'Conflict' }))).toBe('Conflict');
    expect(problemMessage(error(0, null))).toContain('unreachable');
  });

  it('maps field errors to camelCase keys', () => {
    expect(problemFieldErrors(error(400, { errors: { TtlHours: ['Too long'] } }))).toEqual({
      ttlHours: 'Too long',
    });
    expect(problemFieldErrors(error(409, { errors: { Name: ['x'] } }))).toEqual({});
  });
});
