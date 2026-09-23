/** One page of results, matching `PagedResult<T>` on the server. */
export interface PagedResult<T> {
  readonly items: readonly T[];
  readonly page: number;
  readonly pageSize: number;
  /** Absent when the caller asked the server not to count. */
  readonly totalCount?: number;
  readonly totalPages?: number;
}

/** Paging, sorting and search parameters shared by every list endpoint. */
export interface PageRequest {
  page: number;
  pageSize: number;
  /** `-dateOfJoining,displayName`; a leading minus means descending. */
  sort?: string;
  search?: string;
  includeTotal?: boolean;
}

export const DEFAULT_PAGE_SIZE = 25;
export const MAX_PAGE_SIZE = 200;

/**
 * RFC 9457 problem document, as every API error arrives.
 *
 * The UI switches on `errorCode`, never on `detail`: server text can change wording or arrive in
 * another language, while the code is a contract.
 */
export interface ProblemDetails {
  readonly type?: string;
  readonly title?: string;
  readonly status?: number;
  readonly detail?: string;
  readonly instance?: string;
  /** Stable machine-readable code, e.g. `LEAVE_INSUFFICIENT_BALANCE`. */
  readonly errorCode?: string;
  /** Field-level validation failures, keyed by property name. */
  readonly errors?: Readonly<Record<string, readonly string[]>>;
  /** Quote this in support: it ties the UI action to server logs and the audit trail. */
  readonly traceId?: string;
  readonly context?: Readonly<Record<string, unknown>>;
}

/** A long-running server operation accepted with 202. */
export interface JobAccepted {
  readonly jobId: string;
  readonly statusUrl: string;
}
