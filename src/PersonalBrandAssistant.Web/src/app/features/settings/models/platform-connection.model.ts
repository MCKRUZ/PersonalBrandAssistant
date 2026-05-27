export type ConnectionStatus = 'Connected' | 'Expired' | 'NotConfigured';

export interface PlatformStatus {
  platform: string;
  isConnected: boolean;
  status: ConnectionStatus;
  expiresAt: string | null;
  lastPublishDate: string | null;
  capabilities: PlatformCapabilities | null;
}

export interface PlatformCapabilities {
  maxCharacters: number;
  supportsMarkdown: boolean;
  supportsHtml: boolean;
  supportsImages: boolean;
  supportsScheduling: boolean;
  supportsThreads: boolean;
  supportedMediaTypes: string[];
}

export interface StoreCredentialsRequest {
  token?: string;
  email?: string;
  password?: string;
}

export interface ConnectionStatusResponse {
  status: ConnectionStatus;
  expiresAt: string | null;
}

export interface PlatformConfig {
  platform: string;
  displayName: string;
  description: string;
  connectionType: 'oauth' | 'token' | 'login' | 'none';
}
