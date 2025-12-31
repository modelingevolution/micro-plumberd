# MicroPlumberd.Services.Identity.Blazor

Blazor components for user and role management with MicroPlumberd.Services.Identity.

## Installation

```bash
dotnet add package MicroPlumberd.Services.Identity.Blazor
```

## Components

### UsersList

Displays all users with options to add, delete, reset password, and manage roles.

```razor
@using MicroPlumberd.Services.Identity.Blazor.Components

<UsersList OnUserChanged="StateHasChanged" />
```

### RolesList

Displays all roles with options to add, edit, and delete roles.

```razor
@using MicroPlumberd.Services.Identity.Blazor.Components

<RolesList OnRoleChanged="StateHasChanged" />
```

## Requirements

- MicroPlumberd.Services.Identity configured in your application
- ASP.NET Core Identity with UserManager and RoleManager registered
