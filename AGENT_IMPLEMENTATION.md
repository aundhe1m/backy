# Backy Agent Implementation Plan

## Overview
The goal is to separate the drive management functionality from the main Backy application into a standalone "Backy Agent" service that will run on the host machine. This separation will:
1. Enable easier testing through API mocking
2. Allow containerization of the main application
3. Improve system architecture by separating concerns

## Phase 1: Backy Agent Development

### Task 1.1: Create Basic Agent Structure
#### Requirements
- Create new .NET project "Backy.Agent" using minimal API template
- Core configuration parameters:
  - API port (default: 5151)
  - API key for authentication
  - Excluded drives list (e.g., ["/dev/sda"])
- Configuration should be loadable from:
  - appsettings.json
  - Environment variables
  - Command line arguments

#### Implementation Details
1. Project Structure:
   ```
   Backy.Agent/
   ├── Program.cs                 # API setup and DI configuration
   └── appsettings.json          # Configuration file
   ... Add nessecary files here
   ```

2. Security:
   - API key validation on all endpoints
   - Proper error handling for unauthorized access
   - Logging of access attempts

3. System Command Execution:
   - Safe execution of system commands
   - Proper error handling and logging
   - Permission elevation when needed

### Task 1.2: Drive Operations Implementation

#### Implementation Requirements

1. Drive Operations:
   - Use `lsblk` with JSON output for consistent parsing
   - Implement proper drive filtering (excluded drives)
   - Handle drive serial number detection reliably
   - Handle hot-plug events gracefully

2. Pool Operations:
   - Validate mount paths before operations
   - Create proper filesystem after RAID creation
   - Handle pool naming conflicts
   - Implement proper cleanup on failures
   - Prevent duplicate mount path usage
   - Track pool metadata across system reboots using stable GUIDs
   - Auto-validate and repair pool metadata at startup
   - Handle mdadm device reassignments (e.g., md0 becoming md127 after reboot)

3. Process Management:
   - Safe process termination
   - Proper error handling for busy mount points
   - Logging of all process operations

#### Error Handling Strategy
- Define specific error types for common failures
- Include command output in error responses
- Implement proper cleanup for failed operations
- Log all errors with context for debugging

### Task 1.3: API Documentation

#### Swagger Implementation
1. Add Swagger UI with:
   - Detailed operation descriptions
   - Request/response examples
   - Authentication requirements
   - Error response documentation

#### API Documentation Requirements
1. Each endpoint must document:
   - Expected request format
   - All possible response codes
   - Error conditions
   - Example requests/responses
2. Include sequence diagrams for complex operations
3. Document system requirements and permissions

### Task 1.4: Security Implementation
- API key validation middleware
- Rate limiting
- Request validation
- Error handling and logging
- Proper privilege escalation for system commands

### Task 1.5: Service Installation
- Create systemd service file
- Installation/uninstallation scripts
- Configuration file management
- Log rotation setup

### Task 1.6: Pool Metadata Management
#### Implementation Status: Complete

1. Stable Pool Identification:
   - Implemented GUID-based pool tracking for stability across reboots
   - Store metadata mapping in `/var/lib/backy/pool-metadata.json`
   - Maintain drive serial numbers and labels in metadata

2. Automatic Validation:
   - Added validation service at application startup 
   - Implemented hosted service pattern for proper lifecycle management
   - Added API endpoint for on-demand validation
   - Built mapping between drive serials and actual mdadm devices

3. Dynamic Pool Reassembly:
   - Handle mdadm device name changes (e.g., md0 becomes md127)
   - Find next available mdadm device number when mounting
   - Reassemble arrays using component drive identifiers
   - Update metadata automatically with new device names

## Phase 2: Backy Application Refactoring

### Task 2.1: Create API Client

#### Requirements
1. Retry Policy:
   - Exponential backoff
   - Circuit breaker for repeated failures
   - Configurable timeouts
2. Error Handling:
   - Map API errors to application exceptions
   - Handle network failures gracefully
3. Monitoring:
   - Track API response times
   - Log failed requests
   - Monitor circuit breaker status

### Task 2.2: Update DriveService

#### Migration Strategy
1. Create new DriveService implementation:
   - Implement interface using BackyAgentClient
   - Keep same method signatures
   - Add health checking
2. Update dependency injection:
   - Register new implementation
   - Configure client options
3. Add fallback behavior:
   - Monitor agent connection
   - Log connectivity issues
   - Proper error reporting to UI

### Task 2.3: Containerization
- Create Dockerfile for Backy application
- Update docker-compose.yml:

## Phase 3: Mock Agent Development

### Task 3.1: Create Mock Agent

#### Mock Implementation
1. Create standalone container
   - Able to spesify number of mock drives using environment variables
   - Swagger for API testing

2. Mock Data Store:
   - In-memory state management
   - Configurable initial state
   - State persistence between calls

3. Endpoint Behavior:
   - Simulate real timing characteristics
   - Generate realistic error conditions
   - Match real agent response format exactly

#### Testing Support
1. Additional endpoints for test control:
   - Reset state to initial configuration
   - Inject failures or delays
   - Modify drive/pool states
2. Monitoring endpoints:
   - View current mock state
   - View operation history
   - Check injected conditions

## Implementation Order and Testing Strategy

1. Start with Task 1.1 and 1.2
   - Implement core drive operations
   - Manual testing with real drives
   - Unit tests for non-system-calling code

2. Proceed with Task 1.3 and 1.4
   - Document API as it's built
   - Test security measures
   - Integration tests

3. Complete Phase 1 with Task 1.5
   - Test service installation
   - Test logging
   - Test configuration changes

4. Begin Phase 2 migration
   - Implement client first
   - Gradually replace DriveService functionality
   - Test each replacement thoroughly

5. Complete containerization
   - Test container networking
   - Verify mount point access
   - Test with different configurations

6. Implement mock agent
   - Use for automated testing
   - Verify all scenarios
   - Document testing procedures

## Success Criteria

1. Functional Requirements:
   - All current DriveService operations work through agent
   - Proper error handling and recovery

2. Operational Requirements:
   - Proper logging and monitoring
   - Easy configuration management

3. Security Requirements:
   - All communications encrypted
   - Proper authentication
   - Audit logging of operations

4. Testing Requirements:
   - 90% unit test coverage
   - Integration tests for all operations
   - Performance benchmark suite

5. Documentation Requirements:
   - Complete API documentation
   - Operation runbooks
   - Troubleshooting guides