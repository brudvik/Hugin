// Hugin Admin Panel - SignalR Service for Real-time Updates
// Copyright (c) 2024 Hugin Contributors
// Licensed under the MIT License

import { Injectable, inject, signal, computed } from '@angular/core';
import { HubConnection, HubConnectionBuilder, LogLevel, HubConnectionState } from '@microsoft/signalr';
import { AuthService } from './auth.service';
import { LogEntry, RealTimeStats, UserEvent, AdminNotification } from '../models/api.models';

/**
 * Connection state for the SignalR hub
 */
export type ConnectionState = 'disconnected' | 'connecting' | 'connected' | 'reconnecting';

/**
 * SignalR service for real-time admin panel updates.
 * Provides log streaming, live statistics, and event notifications.
 */
@Injectable({
  providedIn: 'root'
})
export class SignalRService {
  private readonly authService = inject(AuthService);
  private hubConnection: HubConnection | null = null;
  
  // State signals
  private readonly _connectionState = signal<ConnectionState>('disconnected');
  private readonly _logs = signal<LogEntry[]>([]);
  private readonly _stats = signal<RealTimeStats | null>(null);
  private readonly _userEvents = signal<UserEvent[]>([]);
  private readonly _notifications = signal<AdminNotification[]>([]);
  
  // Public readonly signals
  readonly connectionState = this._connectionState.asReadonly();
  readonly logs = this._logs.asReadonly();
  readonly stats = this._stats.asReadonly();
  readonly userEvents = this._userEvents.asReadonly();
  readonly notifications = this._notifications.asReadonly();
  
  // Computed values
  readonly isConnected = computed(() => this._connectionState() === 'connected');
  readonly recentLogs = computed(() => this._logs().slice(-100));
  readonly unreadNotifications = computed(() => 
    this._notifications().filter(n => !n.read).length
  );

  private readonly maxLogEntries = 1000;
  private readonly maxUserEvents = 100;

  /**
   * Connects to the SignalR hub.
   */
  async connect(): Promise<void> {
    if (this.hubConnection?.state === HubConnectionState.Connected) {
      return;
    }

    const token = this.authService.getToken();
    if (!token) {
      console.warn('Cannot connect to SignalR: No auth token');
      return;
    }

    this._connectionState.set('connecting');

    try {
      this.hubConnection = new HubConnectionBuilder()
        .withUrl('/api/hubs/admin', {
          accessTokenFactory: () => token
        })
        .withAutomaticReconnect({
          nextRetryDelayInMilliseconds: (retryContext) => {
            // Exponential backoff: 0s, 2s, 4s, 8s, 16s, max 30s
            return Math.min(1000 * Math.pow(2, retryContext.previousRetryCount), 30000);
          }
        })
        .configureLogging(LogLevel.Information)
        .build();

      // Set up event handlers
      this.setupEventHandlers();

      // Start connection
      await this.hubConnection.start();
      this._connectionState.set('connected');
      console.log('SignalR connected');

    } catch (error) {
      console.error('SignalR connection failed:', error);
      this._connectionState.set('disconnected');
      throw error;
    }
  }

  /**
   * Disconnects from the SignalR hub.
   */
  async disconnect(): Promise<void> {
    if (this.hubConnection) {
      try {
        await this.hubConnection.stop();
      } catch (error) {
        console.error('Error disconnecting from SignalR:', error);
      }
      this.hubConnection = null;
      this._connectionState.set('disconnected');
    }
  }

  /**
   * Subscribes to log streaming at the specified level.
   */
  async subscribeToLogs(minLevel: string = 'Information'): Promise<void> {
    if (!this.isConnected()) {
      await this.connect();
    }
    await this.hubConnection?.invoke('SubscribeToLogs', minLevel);
  }

  /**
   * Unsubscribes from log streaming.
   */
  async unsubscribeFromLogs(minLevel: string = 'Information'): Promise<void> {
    await this.hubConnection?.invoke('UnsubscribeFromLogs', minLevel);
  }

  /**
   * Subscribes to real-time statistics updates.
   */
  async subscribeToStats(): Promise<void> {
    if (!this.isConnected()) {
      await this.connect();
    }
    await this.hubConnection?.invoke('SubscribeToStats');
  }

  /**
   * Unsubscribes from statistics updates.
   */
  async unsubscribeFromStats(): Promise<void> {
    await this.hubConnection?.invoke('UnsubscribeFromStats');
  }

  /**
   * Subscribes to user events.
   */
  async subscribeToUserEvents(): Promise<void> {
    if (!this.isConnected()) {
      await this.connect();
    }
    await this.hubConnection?.invoke('SubscribeToUserEvents');
  }

  /**
   * Unsubscribes from user events.
   */
  async unsubscribeFromUserEvents(): Promise<void> {
    await this.hubConnection?.invoke('UnsubscribeFromUserEvents');
  }

  /**
   * Clears the log buffer.
   */
  clearLogs(): void {
    this._logs.set([]);
  }

  /**
   * Marks a notification as read.
   */
  markNotificationRead(index: number): void {
    this._notifications.update(notifications => {
      const updated = [...notifications];
      if (updated[index]) {
        updated[index] = { ...updated[index], read: true };
      }
      return updated;
    });
  }

  /**
   * Clears all notifications.
   */
  clearNotifications(): void {
    this._notifications.set([]);
  }

  private setupEventHandlers(): void {
    if (!this.hubConnection) return;

    // Handle reconnection events
    this.hubConnection.onreconnecting(() => {
      this._connectionState.set('reconnecting');
      console.log('SignalR reconnecting...');
    });

    this.hubConnection.onreconnected(() => {
      this._connectionState.set('connected');
      console.log('SignalR reconnected');
    });

    this.hubConnection.onclose(() => {
      this._connectionState.set('disconnected');
      console.log('SignalR connection closed');
    });

    // Handle log events
    this.hubConnection.on('ReceiveLog', (logEntry: LogEntry) => {
      this._logs.update(logs => {
        const updated = [...logs, logEntry];
        // Keep only the most recent entries
        if (updated.length > this.maxLogEntries) {
          return updated.slice(-this.maxLogEntries);
        }
        return updated;
      });
    });

    // Handle stats updates
    this.hubConnection.on('ReceiveStats', (stats: RealTimeStats) => {
      this._stats.set(stats);
    });

    // Handle user events
    this.hubConnection.on('ReceiveUserEvent', (event: UserEvent) => {
      this._userEvents.update(events => {
        const updated = [...events, event];
        if (updated.length > this.maxUserEvents) {
          return updated.slice(-this.maxUserEvents);
        }
        return updated;
      });
    });

    // Handle notifications
    this.hubConnection.on('ReceiveNotification', (notification: AdminNotification) => {
      this._notifications.update(notifications => [
        { ...notification, read: false },
        ...notifications
      ]);
    });
  }
}
