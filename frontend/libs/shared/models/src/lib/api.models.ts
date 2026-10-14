/** One page of results, matching `PagedResult<T>` on the server. */
export interface PagedResult<T> {
  readonly items: readonly T[];
  readonly page: number;
  readonly pageSize: number;
  /** Absent when the caller asked the server not to count. */
  readonly totalCount?: number;
  readonly totalPages?: number;
  /** Whether another page exists - known even when the total was not counted. */
  readonly hasNextPage?: boolean;
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

/** One invalid field in an error response. `field` is the camelCase JSON path, e.g. `address.city`. */
export interface FieldError {
  readonly field: string;
  readonly message: string;
}

/**
 * RFC 9457 problem document carrying the PeopleOS error envelope, as every API error arrives:
 * `{ success: false, code, message, errors: [{ field, message }], traceId }` alongside the standard
 * `type`, `title`, `status` and `detail`.
 *
 * The UI switches on `code`, never on `message`: server text can change wording or arrive in
 * another language, while the code is a contract.
 */
export interface ProblemDetails {
  readonly type?: string;
  readonly title?: string;
  readonly status?: number;
  readonly detail?: string;
  readonly instance?: string;
  readonly success?: false;
  /** Stable machine-readable code, e.g. `LEAVE_INSUFFICIENT_BALANCE`. */
  readonly code?: string;
  readonly message?: string;
  /** Field-level validation failures, in the order the server reported them. */
  readonly errors?: readonly FieldError[];
  /** Quote this in support: it ties the UI action to server logs and the audit trail. */
  readonly traceId?: string;
  readonly context?: Readonly<Record<string, unknown>>;
}

/** A long-running server operation accepted with 202. */
export interface JobAccepted {
  readonly jobId: string;
  readonly statusUrl: string;
}
