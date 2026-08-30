# Portfolio Summary

This project implements a multiplayer deduction game with role assignment, item interaction, lobby flow, and networked gameplay systems.

## Core systems

- Role management and server-authoritative assignment
- Networked player interaction and inventory flow
- Pickup, drag, placement, and throw mechanics
- Room configuration and match lifecycle management
- Local AI experimentation tools for editor-side asset generation

## Directory overview

- `Scripts/AI/` : local ComfyUI workflow and editor-side experimental AI tooling
- `Scripts/Item/` : pickup, placement, drag, and item-specific logic
- `Scripts/Item/Interface/` : common interfaces for item interaction contracts
- `Scripts/Player/` : player movement, interaction, inventory, role logic, and ragdoll behavior
- `Scripts/Manager/` : game timer, score, and overall match flow
- `Scripts/Network/` : room join/connect flow and networking setup
- `Scripts/RoomSetting/` : lobby and room configuration UI and state
- `Scripts/Settings/` : settings and input binding management
- `Scripts/Chat/` : message sending and receiving logic
- `Scripts/Generic/` : shared singleton and utility classes
- `Scripts/Editor/` : editor-only tools

## Public-facing emphasis

- Interface-based item design for maintainability
- Server-driven role and authority structure
- Network-safe interaction logic and state validation
- AI tools treated as experimental project extensions rather than core gameplay systems
