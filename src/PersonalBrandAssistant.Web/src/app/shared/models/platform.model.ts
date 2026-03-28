import { PlatformType } from './enums';

export interface Platform {
  readonly id: string;
  readonly type: PlatformType;
  readonly displayName: string;
  readonly isConnected: boolean;
  readonly tokenExpiresAt?: string;
  readonly grantedScopes?: readonly string[];
  readonly lastSyncAt?: string;
  readonly version: number;
  readonly createdAt: string;
  readonly updatedAt: string;
}

export interface OAuthCallbackRequest {
  readonly code: string;
  readonly codeVerifier?: string;
  readonly state: string;
}

export interface TestPostRequest {
  readonly confirm: boolean;
  readonly message?: string;
}

export interface AuthUrlResponse {
  readonly authUrl: string;
  readonly state: string;
}
