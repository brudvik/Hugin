# Hugin Admin Panel

The Hugin Admin Panel is a modern web-based interface for configuring and administering the Hugin IRC Server. It features a dark theme, responsive design, and comprehensive management capabilities.

## Features

### Setup Wizard
First-time setup wizard guides you through:
1. **Welcome** - Introduction and feature overview
2. **Server Configuration** - Server name, network, ports, MOTD
3. **TLS Certificate** - Self-signed, existing, or Let's Encrypt
4. **Database** - PostgreSQL connection with test feature
5. **Administrator** - Create the first admin account
6. **Complete** - Summary and confirmation

### Dashboard
Real-time overview of server status:
- Server status banner (online/offline with uptime)
- Statistics cards (users, channels, messages, registered)
- Active channels list with user/message counts
- Connected users list with connection times
- Quick actions (reload config, restart, shutdown)

### User Management
- Browse all connected users with pagination
- Search users by nickname, username, or hostname
- View user details (channels, modes, connection info)
- Send notices to users
- Disconnect users with reason

### Channel Management
- View all channels with topic, modes, and user counts
- Create new channels with custom modes
- Edit channel settings (topic, modes, limits)
- Delete channels
- View channel member lists

### Operator Management
- Configure IRC operators (IRCops)
- Set operator flags and permissions
  - `global` - Full server access
  - `local` - Local server commands
  - `kill` - KILL command access
  - `kline` - K-line management
  - `gline` - G-line management
  - `rehash` - Reload configuration
- Hostmask restrictions for OPER command
- Password reset functionality

### Ban Management
- **K-Lines** - Local server bans by hostmask
- **G-Lines** - Global network bans
- **Z-Lines** - IP-based bans
- Duration settings (1h to permanent)
- Expiration tracking with warnings
- Quick removal

### Configuration
- **Server Settings** - Name, network, ports, max connections
- **Connection Settings** - Ping intervals, timeouts, TLS requirements
- **Rate Limiting** - Messages/commands per second, flood protection
- **Channel Settings** - Limits, creation permissions
- **MOTD Editor** - Edit and preview Message of the Day
- **IRCv3 Capabilities** - Toggle individual caps (message-tags, SASL, etc.)

## Technology Stack

### Frontend
- **Angular 18** - Standalone components with signals
- **Bootstrap 5.3** - Dark theme with CSS variables
- **FontAwesome 6** - Icons throughout the interface
- **ng-bootstrap** - Angular Bootstrap components

### Backend
- **ASP.NET Core** - Web API with Kestrel
- **JWT Authentication** - Bearer tokens with refresh
- **Argon2id** - Password hashing
- **Swagger/OpenAPI** - API documentation

## Configuration

### appsettings.json
```json
{
  "Hugin": {
    "Admin": {
      "Enabled": true,
      "Port": 9443,
      "JwtSecret": "your-256-bit-secret-key-here",
      "AllowedOrigins": ["https://admin.example.com"]
    }
  }
}
```

### Environment Variables
- `HUGIN_ADMIN_JWT_SECRET` - JWT signing key (required)
- `HUGIN_ADMIN_PORT` - Admin panel port (default: 9443)

## Security

### Authentication
- JWT tokens with configurable expiration
- Refresh token rotation
- Argon2id password hashing with salt
- Rate limiting on login attempts

### Authorization
- Role-based access control
  - `Admin` - Full access to all features
  - `Operator` - User/channel management
  - `Moderator` - Limited moderation tools
  - `Viewer` - Read-only access

### Best Practices
1. Use a strong JWT secret (256+ bits)
2. Enable HTTPS only in production
3. Restrict allowed CORS origins
4. Use strong admin passwords
5. Regularly rotate credentials

## API Reference

The admin panel exposes a REST API at `/api/v1/`:

### Authentication
- `POST /api/v1/auth/login` - Login with credentials
- `POST /api/v1/auth/refresh` - Refresh access token
- `POST /api/v1/auth/logout` - Logout and invalidate tokens
- `GET /api/v1/auth/profile` - Get current user profile

### Server Status
- `GET /api/v1/status` - Server status and statistics
- `POST /api/v1/status/restart` - Restart the server
- `POST /api/v1/status/shutdown` - Shutdown the server
- `POST /api/v1/status/reload` - Reload configuration

### Configuration
- `GET /api/v1/config` - Get server configuration
- `PUT /api/v1/config` - Update configuration
- `GET /api/v1/config/motd` - Get MOTD
- `PUT /api/v1/config/motd` - Update MOTD

### Users
- `GET /api/v1/users` - List users (paginated)
- `GET /api/v1/users/{nickname}` - Get user details
- `DELETE /api/v1/users/{nickname}` - Disconnect user
- `POST /api/v1/users/{nickname}/message` - Send message/notice

### Channels
- `GET /api/v1/channels` - List channels (paginated)
- `GET /api/v1/channels/{name}` - Get channel details
- `POST /api/v1/channels` - Create channel
- `PUT /api/v1/channels/{name}` - Update channel
- `DELETE /api/v1/channels/{name}` - Delete channel

### Operators
- `GET /api/v1/operators` - List operators
- `POST /api/v1/operators` - Create operator
- `PUT /api/v1/operators/{name}` - Update operator
- `DELETE /api/v1/operators/{name}` - Delete operator

### Bans
- `GET /api/v1/bans` - List all bans
- `POST /api/v1/bans` - Create ban
- `PUT /api/v1/bans/{id}` - Update ban
- `DELETE /api/v1/bans/{id}` - Remove ban

## Development

### Building the Frontend
```bash
cd src/Hugin.Server/ClientApp
npm install
npm run build
```

### Development Server
```bash
npm run start
```
The Angular dev server runs on `http://localhost:4200` with proxy to the API.

### Production Build
```bash
npm run build -- --configuration production
```
Output is placed in `dist/hugin-admin` and served by ASP.NET Core.

## Troubleshooting

### Cannot access admin panel
1. Check that admin port is open in firewall
2. Verify `Admin.Enabled` is true in configuration
3. Check browser console for CORS errors

### Login fails
1. Verify credentials are correct
2. Check server logs for authentication errors
3. Ensure JWT secret is configured

### API errors
1. Check browser Network tab for response details
2. Review server logs for exceptions
3. Verify API endpoint paths are correct
