// Hugin Admin Panel - API Models
export interface ApiResponse<T> {
  success: boolean;
  data?: T;
  error?: string;
  validationErrors?: { [key: string]: string[] };
}

export interface LoginRequest {
  username: string;
  password: string;
}

export interface LoginResponse {
  token: string;
  expiresIn: number;
  refreshToken?: string;
  displayName: string;
  roles: string[];
}

export interface AdminUser {
  id: string;
  username: string;
  displayName: string;
  email: string;
  roles: string[];
  createdAt: string;
  lastLoginAt?: string;
}

export interface SetupState {
  isConfigured: boolean;
  currentStep?: SetupStep;
  steps: SetupStep[];
}

export interface SetupStep {
  currentStep: number;
  totalSteps: number;
  title: string;
  description: string;
  isComplete: boolean;
  setupComplete: boolean;
}

export interface SetupRequired {
  setupRequired: boolean;
  hasAdminUser: boolean;
  hasConfiguration: boolean;
}

export interface ServerStatus {
  serverName: string;
  networkName: string;
  version: string;
  uptime: number; // TimeSpan serialized as total seconds
  isRunning: boolean;
  connectedUsers: number;
  activeChannels: number;
  channelCount: number;
  operatorsOnline: number;
  registeredUsers?: number;
  messagesProcessed?: number;
  tlsPort: number;
  maxConnections: number;
  enabledCapabilities: string[];
  statistics?: ServerStatistics;
}

export interface ServerStatistics {
  totalConnections: number;
  totalMessages: number;
  peakUsers: number;
  memoryUsageBytes: number;
  cpuUsagePercent: number;
  messagesPerSecond: number;
}

export interface ServerConfig {
  serverName: string;
  networkName: string;
  description?: string;
  adminEmail?: string;
  tlsPort: number;
  adminPort: number;
  maxConnections: number;
  maxUsers?: number;
  maxChannelsPerUser: number;
  maxUsersPerChannel?: number;
  maxNickLength?: number;
  maxChannelLength?: number;
  maxTopicLength: number;
  pingInterval?: number;
  pingTimeout?: number;
  registrationTimeout?: number;
  requireTls?: boolean;
  cloakHostnames?: boolean;
  messagesPerSecond?: number;
  connectionsPerMinute?: number;
  commandsPerSecond?: number;
  enableFloodProtection?: boolean;
  allowChannelCreation?: boolean;
  motd?: string;
  enabledCapabilities?: string[];
  ports?: PortConfig;
  tls?: TlsConfig;
}

export interface PortConfig {
  tlsPort: number;
  webSocketPort: number;
  adminPort: number;
  plaintextPort: number;
}

export interface TlsConfig {
  certificatePath?: string;
  hasValidCertificate: boolean;
  certificateSubject?: string;
  certificateExpiry?: string;
  useLetsEncrypt: boolean;
  letsEncryptEmail?: string;
}

export interface SetupServerRequest {
  serverName: string;
  networkName: string;
  description?: string;
  adminEmail?: string;
}

export interface SetupTlsRequest {
  method: 'Upload' | 'LetsEncrypt' | 'SelfSigned' | 'Skip';
  certificateBase64?: string;
  certificatePassword?: string;
  letsEncryptEmail?: string;
  letsEncryptDomains?: string[];
}

export interface SetupDatabaseRequest {
  host: string;
  port: number;
  database: string;
  username: string;
  password: string;
  useSsl: boolean;
}

export interface DatabaseTestResult {
  success: boolean;
  error?: string;
  postgresVersion?: string;
  databaseExists: boolean;
  needsMigration: boolean;
}

export interface SetupAdminRequest {
  username: string;
  password: string;
  confirmPassword: string;
  email: string;
  ircOperName?: string;
}

export interface TlsSetupResult {
  success: boolean;
  certificateSubject?: string;
  certificateExpiry?: string;
  error?: string;
  warning?: string;
}

export interface User {
  nickname: string;
  username: string;
  hostname: string;
  realName: string;
  account?: string;
  isOperator: boolean;
  isRegistered?: boolean;
  isOnline?: boolean;
  modes: string;
  channels: string[];
  connectedAt: string;
  lastActivity: string;
  isAway: boolean;
  awayMessage?: string;
  isSecure: boolean;
  realIp?: string;
  idleTime?: number;
}

export interface Channel {
  name: string;
  topic?: string;
  topicSetBy?: string;
  topicSetAt?: string;
  modes: string;
  userCount: number;
  messageCount?: number;
  createdAt: string;
  isRegistered: boolean;
  isPrivate?: boolean;
  isSecret?: boolean;
  isModerated?: boolean;
  isInviteOnly?: boolean;
  userLimit?: number;
  founder?: string;
  users?: string[];
}

export interface ChannelMember {
  nickname: string;
  prefixes: string;
  joinedAt: string;
  isAway: boolean;
}

export interface Operator {
  name: string;
  username: string;
  email?: string;
  password?: string;
  operClass?: string;
  hostmask?: string;
  hostmasks?: string[];
  isOnline: boolean;
  lastSeen?: string;
  permissions?: string[];
  flags?: string[];
}

export interface ServerBan {
  id?: string;
  type: 'kline' | 'gline' | 'zline';
  mask: string;
  reason: string;
  setBy: string;
  setAt: string;
  expiresAt?: string;
  isPermanent?: boolean;
  affectedCount?: number;
}

export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

// Real-time and Logging models

export interface LogEntry {
  id: string;
  timestamp: string;
  level: 'Trace' | 'Debug' | 'Information' | 'Warning' | 'Error' | 'Critical';
  message: string;
  exception?: string;
  source?: string;
  properties?: { [key: string]: string };
}

export interface LogsResponse {
  entries: LogEntry[];
  totalCount: number;
  hasMore: boolean;
}

export interface LogFileInfo {
  name: string;
  path: string;
  sizeBytes: number;
  lastModified: string;
  created: string;
}

export interface LogFilesResponse {
  files: LogFileInfo[];
  logDirectory: string;
}

export interface LogFileContentResponse {
  fileName: string;
  content: string;
  offset: number;
  bytesRead: number;
  totalSize: number;
  hasMore: boolean;
}

export interface LogCleanupResponse {
  deletedFiles: string[];
  deletedCount: number;
  freedBytes: number;
}

export interface RealTimeStats {
  timestamp: string;
  connectedUsers: number;
  channelCount: number;
  operatorsOnline: number;
  messagesPerSecond: number;
  bytesInPerSecond: number;
  bytesOutPerSecond: number;
  memoryUsageMb: number;
  cpuUsagePercent: number;
  activeConnections: number;
  pendingOperations: number;
}

export interface UserEvent {
  eventType: 'Connected' | 'Disconnected' | 'NickChange' | 'Join' | 'Part' | 'Quit';
  timestamp: string;
  nickname: string;
  userId?: string;
  hostname?: string;
  channel?: string;
  details?: string;
}

export interface AdminNotification {
  type: 'Info' | 'Warning' | 'Error' | 'Success';
  title: string;
  message: string;
  timestamp: string;
  persistent?: boolean;
  actionUrl?: string;
  read?: boolean;
}

