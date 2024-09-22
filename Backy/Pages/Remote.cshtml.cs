using Backy.Data;
using Backy.Models;
using Backy.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Renci.SshNet;

namespace Backy.Pages
{
    public class RemoteModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly IDataProtector _protector;
        private readonly ILogger<RemoteModel> _logger;

        public RemoteModel(ApplicationDbContext context, IDataProtectionProvider provider, ILogger<RemoteModel> logger)
        {
            _context = context;
            _protector = provider.CreateProtector("Backy.RemoteStorage");
            _logger = logger;
        }

        public IList<RemoteStorage> RemoteStorages { get; set; } = new List<RemoteStorage>();

        [BindProperty]
        public RemoteStorage RemoteStorage { get; set; } = new RemoteStorage();

        public async Task OnGetAsync()
        {
            RemoteStorages = await _context.RemoteStorages.ToListAsync();

            // Check the status of each storage
            foreach (var storage in RemoteStorages)
            {
                await StorageStatusChecker.CheckAndUpdateStorageStatusAsync(storage, _context, _protector, _logger);
            }
        }

        public async Task<JsonResult> OnGetGetStorageAsync(int id)
        {
            var storage = await _context.RemoteStorages.FindAsync(id);
            if (storage == null)
            {
                return new JsonResult(new { success = false });
            }

            // Decrypt sensitive data
            if (!string.IsNullOrEmpty(storage.Password))
            {
                storage.Password = Decrypt(storage.Password);
            }
            if (!string.IsNullOrEmpty(storage.SSHKey))
            {
                storage.SSHKey = Decrypt(storage.SSHKey);
            }

            // Return data without exposing sensitive information
            var result = new
            {
                id = storage.Id,
                name = storage.Name,
                host = storage.Host,
                port = storage.Port,
                username = storage.Username,
                authenticationMethod = storage.AuthenticationMethod,
                remotePath = storage.RemotePath
            };
            return new JsonResult(result);
        }

        public async Task<IActionResult> OnPostAddAsync()
        {
            _logger.LogInformation("Adding new storage: {Name}", RemoteStorage.Name);

            // Initial ModelState validation
            if (!ModelState.IsValid)
            {
                _logger.LogWarning("Model state is invalid.");

                foreach (var key in ModelState.Keys)
                {
                    var state = ModelState[key];
                    foreach (var error in state.Errors)
                    {
                        _logger.LogWarning("ModelState Error - Key: {Key}, Error: {ErrorMessage}", key, error.ErrorMessage);
                    }
                }

                // Re-populate the RemoteStorages list for the page
                RemoteStorages = await _context.RemoteStorages.ToListAsync();

                // Return the page with ModelState errors
                return Page();
            }

            // Custom validation
            if (RemoteStorage.AuthenticationMethod == "Password")
            {
                if (string.IsNullOrWhiteSpace(RemoteStorage.Password))
                {
                    ModelState.AddModelError(nameof(RemoteStorage.Password), "Password is required when using Password authentication.");
                }
            }
            else if (RemoteStorage.AuthenticationMethod == "SSH Key")
            {
                if (string.IsNullOrWhiteSpace(RemoteStorage.SSHKey))
                {
                    ModelState.AddModelError(nameof(RemoteStorage.SSHKey), "SSH Key is required when using SSH Key authentication.");
                }
            }
            else
            {
                ModelState.AddModelError(nameof(RemoteStorage.AuthenticationMethod), "Invalid Authentication Method.");
            }

            // Re-check ModelState after custom validation
            if (!ModelState.IsValid)
            {
                _logger.LogWarning("Custom validation failed.");

                // Re-populate the RemoteStorages list for the page
                RemoteStorages = await _context.RemoteStorages.ToListAsync();

                // Return the page with ModelState errors
                return Page();
            }

            // Set Id to 0 to ensure EF Core treats it as a new entity
            RemoteStorage.Id = 0;

            // Encrypt sensitive data
            if (RemoteStorage.AuthenticationMethod == "Password" && !string.IsNullOrEmpty(RemoteStorage.Password))
            {
                RemoteStorage.Password = Encrypt(RemoteStorage.Password);
            }
            else if (RemoteStorage.AuthenticationMethod == "SSH Key" && !string.IsNullOrEmpty(RemoteStorage.SSHKey))
            {
                RemoteStorage.SSHKey = Encrypt(RemoteStorage.SSHKey);
            }

            // Validate the connection
            bool isValid = ValidateConnection(RemoteStorage);
            if (!isValid)
            {
                _logger.LogWarning("Connection validation failed for storage: {Name}", RemoteStorage.Name);
                ModelState.AddModelError(string.Empty, "Unable to connect with the provided details.");

                // Re-populate the RemoteStorages list for the page
                RemoteStorages = await _context.RemoteStorages.ToListAsync();

                return Page();
            }

            _context.RemoteStorages.Add(RemoteStorage);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Storage added successfully: {Name}", RemoteStorage.Name);

            // Check and update storage status
            await StorageStatusChecker.CheckAndUpdateStorageStatusAsync(RemoteStorage, _context, _protector, _logger);

            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostEditAsync()
        {
            _logger.LogInformation("Editing storage: {Id}", RemoteStorage.Id);

            if (!ModelState.IsValid)
            {
                _logger.LogWarning("Model state is invalid.");

                foreach (var key in ModelState.Keys)
                {
                    var state = ModelState[key];
                    foreach (var error in state.Errors)
                    {
                        _logger.LogWarning("ModelState Error - Key: {Key}, Error: {ErrorMessage}", key, error.ErrorMessage);
                    }
                }

                // Re-populate the RemoteStorages list for the page
                RemoteStorages = await _context.RemoteStorages.ToListAsync();

                // Return the page with ModelState errors
                return Page();
            }

            // Custom validation
            if (RemoteStorage.AuthenticationMethod == "Password")
            {
                if (string.IsNullOrWhiteSpace(RemoteStorage.Password))
                {
                    ModelState.AddModelError(nameof(RemoteStorage.Password), "Password is required when using Password authentication.");
                }
            }
            else if (RemoteStorage.AuthenticationMethod == "SSH Key")
            {
                if (string.IsNullOrWhiteSpace(RemoteStorage.SSHKey))
                {
                    ModelState.AddModelError(nameof(RemoteStorage.SSHKey), "SSH Key is required when using SSH Key authentication.");
                }
            }
            else
            {
                ModelState.AddModelError(nameof(RemoteStorage.AuthenticationMethod), "Invalid Authentication Method.");
            }

            // Re-check ModelState after custom validation
            if (!ModelState.IsValid)
            {
                _logger.LogWarning("Custom validation failed.");

                // Re-populate the RemoteStorages list for the page
                RemoteStorages = await _context.RemoteStorages.ToListAsync();

                // Return the page with ModelState errors
                return Page();
            }

            var existingStorage = await _context.RemoteStorages.FindAsync(RemoteStorage.Id);
            if (existingStorage == null)
            {
                _logger.LogWarning("Storage not found: {Id}", RemoteStorage.Id);
                return NotFound();
            }

            // Update fields
            existingStorage.Name = RemoteStorage.Name;
            existingStorage.Host = RemoteStorage.Host;
            existingStorage.Port = RemoteStorage.Port;
            existingStorage.Username = RemoteStorage.Username;
            existingStorage.AuthenticationMethod = RemoteStorage.AuthenticationMethod;
            existingStorage.RemotePath = RemoteStorage.RemotePath;

            // Encrypt sensitive data
            if (RemoteStorage.AuthenticationMethod == "Password")
            {
                if (!string.IsNullOrEmpty(RemoteStorage.Password) && RemoteStorage.Password != "********")
                {
                    existingStorage.Password = Encrypt(RemoteStorage.Password);
                }
                // If '********', do not change the password
            }
            else if (RemoteStorage.AuthenticationMethod == "SSH Key")
            {
                if (!string.IsNullOrEmpty(RemoteStorage.SSHKey) && RemoteStorage.SSHKey != "********")
                {
                    existingStorage.SSHKey = Encrypt(RemoteStorage.SSHKey);
                }
                // If '********', do not change the SSH Key
            }

            // Validate the connection
            bool isValid = ValidateConnection(existingStorage);
            if (!isValid)
            {
                _logger.LogWarning("Connection validation failed for storage: {Name}", existingStorage.Name);
                ModelState.AddModelError(string.Empty, "Unable to connect with the provided details.");

                // Re-populate the RemoteStorages list for the page
                RemoteStorages = await _context.RemoteStorages.ToListAsync();

                return Page();
            }

            try
            {
                await _context.SaveChangesAsync();
                _logger.LogInformation("Storage updated successfully: {Name}", existingStorage.Name);

                // Check and update storage status
                await StorageStatusChecker.CheckAndUpdateStorageStatusAsync(existingStorage, _context, _protector, _logger);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                _logger.LogError(ex, "Error updating storage: {Id}", RemoteStorage.Id);
                if (!RemoteStorageExists(RemoteStorage.Id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }

            await StorageStatusChecker.CheckAndUpdateStorageStatusAsync(existingStorage, _context, _protector, _logger);

            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostDeleteAsync(int id)
        {
            _logger.LogInformation("Deleting storage: {Id}", id);
            var storage = await _context.RemoteStorages.FindAsync(id);
            if (storage != null)
            {
                _context.RemoteStorages.Remove(storage);
                await _context.SaveChangesAsync();
                _logger.LogInformation("Storage deleted successfully: {Id}", id);
            }
            else
            {
                _logger.LogWarning("Storage not found: {Id}", id);
            }
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostToggleEnableAsync(int id)
        {
            _logger.LogInformation("Toggling enable status for storage: {Id}", id);
            var storage = await _context.RemoteStorages.FindAsync(id);
            if (storage != null)
            {
                storage.IsEnabled = !storage.IsEnabled;
                await _context.SaveChangesAsync();
                _logger.LogInformation("Storage enable status updated: {Id}, IsEnabled: {IsEnabled}", id, storage.IsEnabled);
            }
            else
            {
                _logger.LogWarning("Storage not found: {Id}", id);
            }
            return RedirectToPage();
        }

        private bool RemoteStorageExists(int id)
        {
            return _context.RemoteStorages.Any(e => e.Id == id);
        }

        private string Encrypt(string input)
        {
            return _protector.Protect(input);
        }

        private string Decrypt(string? input)
        {
            return input != null ? _protector.Unprotect(input) : string.Empty;
        }

        private bool ValidateConnection(RemoteStorage storage)
        {
            try
            {
                using var client = CreateSftpClient(storage);
                client.Connect();
                bool isConnected = client.IsConnected;
                client.Disconnect();
                return isConnected;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Connection validation failed for storage: {Name}", storage.Name);
                return false;
            }
        }

        private SftpClient CreateSftpClient(RemoteStorage storage)
        {
            if (storage.AuthenticationMethod == "Password")
            {
                return new SftpClient(storage.Host, storage.Port, storage.Username, Decrypt(storage.Password));
            }
            else
            {
                using var keyStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(Decrypt(storage.SSHKey)));
                var keyFile = new PrivateKeyFile(keyStream);
                var keyFiles = new[] { keyFile };
                var authMethod = new PrivateKeyAuthenticationMethod(storage.Username, keyFiles);
                var connectionInfo = new Renci.SshNet.ConnectionInfo(storage.Host, storage.Port, storage.Username, authMethod);
                return new SftpClient(connectionInfo);
            }
        }
    }
}
