// Hugin Admin Panel - API Service
import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable, map } from 'rxjs';
import { environment } from '@env/environment';
import { 
  ApiResponse, 
  ServerStatus, 
  ServerStatistics,
  ServerConfig,
  User,
  Channel,
  ChannelMember,
  Operator,
  ServerBan,
  PagedResult
} from '../models/api.models';

@Injectable({
  providedIn: 'root'
})
export class ApiService {
  private http = inject(HttpClient);
  private baseUrl = environment.apiUrl;

  // Status endpoints
  getStatus(): Observable<ServerStatus> {
    return this.http.get<ApiResponse<ServerStatus>>(`${this.baseUrl}/status`)
      .pipe(map(r => r.data!));
  }

  getServerStatus(): Observable<ServerStatus> {
    return this.getStatus();
  }

  getStatistics(): Observable<ServerStatistics> {
    return this.http.get<ApiResponse<ServerStatistics>>(`${this.baseUrl}/status/statistics`)
      .pipe(map(r => r.data!));
  }

  restartServer(): Observable<boolean> {
    return this.http.post<ApiResponse<void>>(`${this.baseUrl}/status/restart`, {})
      .pipe(map(r => r.success));
  }

  shutdownServer(reason?: string): Observable<boolean> {
    const params = reason ? new HttpParams().set('reason', reason) : undefined;
    return this.http.post<ApiResponse<void>>(`${this.baseUrl}/status/shutdown`, {}, { params })
      .pipe(map(r => r.success));
  }

  reloadConfig(): Observable<boolean> {
    return this.http.post<ApiResponse<void>>(`${this.baseUrl}/status/reload`, {})
      .pipe(map(r => r.success));
  }

  // Config endpoints
  getConfig(): Observable<ServerConfig> {
    return this.http.get<ApiResponse<ServerConfig>>(`${this.baseUrl}/config`)
      .pipe(map(r => r.data!));
  }

  updateConfig(config: ServerConfig): Observable<boolean> {
    return this.http.put<ApiResponse<void>>(`${this.baseUrl}/config`, config)
      .pipe(map(r => r.success));
  }

  getMotd(): Observable<string> {
    return this.http.get<ApiResponse<{ content: string }>>(`${this.baseUrl}/config/motd`)
      .pipe(map(r => r.data?.content ?? ''));
  }

  updateMotd(content: string): Observable<boolean> {
    return this.http.put<ApiResponse<void>>(`${this.baseUrl}/config/motd`, { content })
      .pipe(map(r => r.success));
  }

  // User endpoints
  getUsers(page = 1, pageSize = 50, search?: string): Observable<PagedResult<User>> {
    let params = new HttpParams()
      .set('page', page)
      .set('pageSize', pageSize);
    
    if (search) {
      params = params.set('search', search);
    }

    return this.http.get<ApiResponse<PagedResult<User>>>(`${this.baseUrl}/users`, { params })
      .pipe(map(r => r.data!));
  }

  getUser(nickname: string): Observable<User> {
    return this.http.get<ApiResponse<User>>(`${this.baseUrl}/users/${encodeURIComponent(nickname)}`)
      .pipe(map(r => r.data!));
  }

  killUser(nickname: string, reason?: string): Observable<boolean> {
    const params = reason ? new HttpParams().set('reason', reason) : undefined;
    return this.http.delete<ApiResponse<void>>(`${this.baseUrl}/users/${encodeURIComponent(nickname)}`, { params })
      .pipe(map(r => r.success));
  }

  sendMessage(nickname: string, message: string, asNotice = false): Observable<boolean> {
    return this.http.post<ApiResponse<void>>(`${this.baseUrl}/users/${encodeURIComponent(nickname)}/message`, { message, asNotice })
      .pipe(map(r => r.success));
  }

  sendNotice(nickname: string, message: string): Observable<boolean> {
    return this.sendMessage(nickname, message, true);
  }

  disconnectUser(nickname: string, reason?: string): Observable<boolean> {
    return this.killUser(nickname, reason);
  }

  // Channel endpoints
  getChannels(page = 1, pageSize = 50, search?: string): Observable<PagedResult<Channel>> {
    let params = new HttpParams()
      .set('page', page)
      .set('pageSize', pageSize);
    
    if (search) {
      params = params.set('search', search);
    }

    return this.http.get<ApiResponse<PagedResult<Channel>>>(`${this.baseUrl}/channels`, { params })
      .pipe(map(r => r.data!));
  }

  getChannel(name: string): Observable<Channel> {
    return this.http.get<ApiResponse<Channel>>(`${this.baseUrl}/channels/${encodeURIComponent(name)}`)
      .pipe(map(r => r.data!));
  }

  createChannel(channel: Partial<Channel>): Observable<Channel> {
    return this.http.post<ApiResponse<Channel>>(`${this.baseUrl}/channels`, channel)
      .pipe(map(r => r.data!));
  }

  updateChannel(name: string, channel: Partial<Channel>): Observable<boolean> {
    return this.http.put<ApiResponse<void>>(`${this.baseUrl}/channels/${encodeURIComponent(name)}`, channel)
      .pipe(map(r => r.success));
  }

  deleteChannel(name: string): Observable<boolean> {
    return this.http.delete<ApiResponse<void>>(`${this.baseUrl}/channels/${encodeURIComponent(name)}`)
      .pipe(map(r => r.success));
  }

  getChannelMembers(name: string): Observable<ChannelMember[]> {
    return this.http.get<ApiResponse<ChannelMember[]>>(`${this.baseUrl}/channels/${encodeURIComponent(name)}/members`)
      .pipe(map(r => r.data!));
  }

  setChannelTopic(name: string, topic: string): Observable<boolean> {
    return this.http.put<ApiResponse<void>>(`${this.baseUrl}/channels/${encodeURIComponent(name)}/topic`, { topic })
      .pipe(map(r => r.success));
  }

  // Operator endpoints
  getOperators(): Observable<Operator[]> {
    return this.http.get<ApiResponse<Operator[]>>(`${this.baseUrl}/operators`)
      .pipe(map(r => r.data!));
  }

  createOperator(operator: Partial<Operator> & { password?: string }): Observable<Operator> {
    return this.http.post<ApiResponse<Operator>>(`${this.baseUrl}/operators`, operator)
      .pipe(map(r => r.data!));
  }

  updateOperator(name: string, operator: Partial<Operator>): Observable<boolean> {
    return this.http.put<ApiResponse<void>>(`${this.baseUrl}/operators/${encodeURIComponent(name)}`, operator)
      .pipe(map(r => r.success));
  }

  deleteOperator(name: string): Observable<boolean> {
    return this.http.delete<ApiResponse<void>>(`${this.baseUrl}/operators/${encodeURIComponent(name)}`)
      .pipe(map(r => r.success));
  }

  // Ban endpoints
  getBans(page = 1, pageSize = 50, type?: string): Observable<ServerBan[]> {
    let params = new HttpParams()
      .set('page', page)
      .set('pageSize', pageSize);
    
    if (type) {
      params = params.set('type', type);
    }

    return this.http.get<ApiResponse<ServerBan[]>>(`${this.baseUrl}/bans`, { params })
      .pipe(map(r => r.data!));
  }

  createBan(ban: { type: string; mask: string; reason: string; duration?: number | null; isPermanent?: boolean }): Observable<ServerBan> {
    return this.http.post<ApiResponse<ServerBan>>(`${this.baseUrl}/bans`, ban)
      .pipe(map(r => r.data!));
  }

  updateBan(id: string, ban: Partial<ServerBan>): Observable<boolean> {
    return this.http.put<ApiResponse<void>>(`${this.baseUrl}/bans/${id}`, ban)
      .pipe(map(r => r.success));
  }

  deleteBan(id: string): Observable<boolean> {
    return this.http.delete<ApiResponse<void>>(`${this.baseUrl}/bans/${id}`)
      .pipe(map(r => r.success));
  }

  removeBan(id: string): Observable<boolean> {
    return this.deleteBan(id);
  }
}
