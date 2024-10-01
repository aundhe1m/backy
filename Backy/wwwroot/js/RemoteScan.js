// Initialize tooltips
document.addEventListener('DOMContentLoaded', function () {
    var tooltipTriggerList = [].slice.call(document.querySelectorAll('[data-bs-toggle="tooltip"]'));
    tooltipTriggerList.map(function (tooltipTriggerEl) {
        return new bootstrap.Tooltip(tooltipTriggerEl);
    });

    // Handle displaying toast after page reload based on sessionStorage flag
    const toastData = sessionStorage.getItem('toastMessage');
    if (toastData) {
        const { message, isSuccess } = JSON.parse(toastData);
        showToast(message, isSuccess);
        sessionStorage.removeItem('toastMessage');
    }
});

// Function to toggle enable/disable
function toggleEnable(id) {
    fetch(`/RemoteScan?handler=ToggleEnable&id=${id}`, {
        method: 'POST',
        headers: {
            'X-Requested-With': 'XMLHttpRequest',
            'RequestVerificationToken': document.querySelector('input[name="__RequestVerificationToken"]').value
        }
    })
        .then(response => response.json())
        .then(data => {
            if (data.success) {
                showToast(`Storage status changed.`, true);
            } else {
                showToast(`Storage failed to change: ${data.message}`, false);
            }
        })
        .catch(error => {
            showToast(`An error occurred while updating the storage status: ${error}`, false);
        });
}

// Function to start indexing
function startIndexing(id) {
    const startIndexingButton = document.getElementById(`startIndexingButton-${id}`);
    const startIndexingIcon = document.getElementById(`startIndexingIcon-${id}`);

    // Disable the button and add rotating class to icon
    if (startIndexingButton && startIndexingIcon) {
        startIndexingButton.disabled = true;
        startIndexingIcon.classList.add('rotating');
    }

    fetch(`/RemoteScan?handler=StartIndexing&id=${id}`, {
        method: 'POST',
        headers: {
            'X-Requested-With': 'XMLHttpRequest',
            'RequestVerificationToken': document.querySelector('input[name="__RequestVerificationToken"]').value
        }
    })
        .then(response => response.json())
        .then(data => {
            if (data.success) {
                showToast(`Indexing started successfully.`, true);
            } else {
                showToast(`Failed to start indexing: ${data.message}`, false);
                // Re-enable the button and remove rotating class
                if (startIndexingButton && startIndexingIcon) {
                    startIndexingButton.disabled = false;
                    startIndexingIcon.classList.remove('rotating');
                }
            }
        })
        .catch(error => {
            showToast(`Error starting indexing: ${error}`, false);
            // Re-enable the button and remove rotating class
            if (startIndexingButton && startIndexingIcon) {
                startIndexingButton.disabled = false;
                startIndexingIcon.classList.remove('rotating');
            }
        });
}

setInterval(updateStorageSources, 5000);

// Function to update storage sources periodically
function updateStorageSources() {
    fetch('/RemoteScan?handler=UpdateStorageSources')
        .then(response => response.json())
        .then(data => {
            if (data.success) {
                data.storageSources.forEach(source => {
                    // Update BackupPercentage
                    const backupPercentageElement = document.getElementById(`backupPercentage-${source.id}`);
                    if (backupPercentageElement) {
                        backupPercentageElement.textContent = `${source.backupPercentage}% Backup`;
                    }

                    // Update progress bar
                    const progressBarElement = document.getElementById(`progressBar-${source.id}`);
                    if (progressBarElement) {
                        progressBarElement.style.width = `${source.backupPercentage}%`;
                        progressBarElement.setAttribute('aria-valuenow', source.backupPercentage);
                    }

                    // Update File and Backup Info table
                    const totalFilesElement = document.getElementById(`totalFiles-${source.id}`);
                    if (totalFilesElement) {
                        totalFilesElement.textContent = source.totalFiles;
                    }

                    const backupCountElement = document.getElementById(`backupCount-${source.id}`);
                    if (backupCountElement) {
                        backupCountElement.textContent = source.backupCount;
                    }

                    const totalSizeElement = document.getElementById(`totalSize-${source.id}`);
                    if (totalSizeElement) {
                        totalSizeElement.textContent = formatSize(source.totalSize);
                    }

                    const totalBackupSizeElement = document.getElementById(`totalBackupSize-${source.id}`);
                    if (totalBackupSizeElement) {
                        totalBackupSizeElement.textContent = formatSize(source.totalBackupSize);
                    }

                    // Update IsIndexing state
                    const startIndexingButton = document.getElementById(`startIndexingButton-${source.id}`);
                    const startIndexingIcon = document.getElementById(`startIndexingIcon-${source.id}`);
                    if (startIndexingButton && startIndexingIcon) {
                        if (source.isIndexing) {
                            startIndexingButton.disabled = true;
                            startIndexingIcon.classList.add('rotating');
                        } else {
                            startIndexingButton.disabled = false;
                            startIndexingIcon.classList.remove('rotating');
                        }
                    }
                });
            }
        })
        .catch(error => {
            showToast(`Error updating storage sources: ${error}`, false);
        });
}

// Scripts for Add/Edit Modal
function openAddModal() {
    clearModalFields();
    $('#storageModalLabel').text('Add New Storage Source');
    $('#modalSubmitButton').text('Add Storage');
    $('#storageForm').attr('action', '?handler=Add');
    $('#storageModal').modal('show');
}

function openEditModal(id) {
    clearModalFields();
    $.ajax({
        url: '?handler=GetStorage',
        type: 'GET',
        data: { id: id },
        success: function (data) {
            if (data.success === false) {
                showToast('Storage not found.', false);
                return;
            }

            $('#storageModalLabel').text('Edit Storage Source');
            $('#modalSubmitButton').text('Save Changes');
            $('#storageForm').attr('action', '?handler=Edit');
            $('#storageId').val(data.id);
            $('#storageName').val(data.name);
            $('#storageHost').val(data.host);
            $('#storagePort').val(data.port);
            $('#storageUsername').val(data.username);
            $('#authMethod').val(data.authenticationMethod);
            $('#storageRemotePath').val(data.remotePath);
            toggleAuthFields();
            if (data.authenticationMethod === 'Password') {
                if (data.passwordSet) {
                    $('#storagePassword').val('********'); // Indicate password is set
                } else {
                    $('#storagePassword').val('');
                }
                $('#storageSSHKey').val('');
            } else if (data.authenticationMethod === 'SSH Key') {
                $('#storagePassword').val('');
                if (data.sshKeySet) {
                    $('#storageSSHKey').val('********'); // Indicate SSH key is set
                } else {
                    $('#storageSSHKey').val('');
                }
            }
            $('#storageModal').modal('show');
        },
        error: function () {
            showToast('Error fetching storage details.', false);
        }
    });
}

function closeModal() {
    $('#storageModal').modal('hide');
}

function clearModalFields() {
    $('#storageForm')[0].reset();
    $('#storagePassword').val('');
    $('#storageSSHKey').val('');
    $('.alert-danger').remove();
    toggleAuthFields();
}

function toggleAuthFields() {
    var method = $('#authMethod').val();
    if (method === 'Password') {
        $('#passwordField').show();
        $('#sshKeyField').hide();
        $('#storagePassword').prop('required', true);
        $('#storageSSHKey').prop('required', false);
    } else if (method === 'SSH Key') {
        $('#passwordField').hide();
        $('#sshKeyField').show();
        $('#storagePassword').prop('required', false);
        $('#storageSSHKey').prop('required', true);
    } else {
        $('#passwordField').hide();
        $('#sshKeyField').hide();
        $('#storagePassword').prop('required', false);
        $('#storageSSHKey').prop('required', false);
    }
}

// Ensure the correct fields are shown/hidden on page load
$(document).ready(function () {
    toggleAuthFields();

    // Open the modal if there are ModelState errors
    if ($('.alert-danger').length > 0) {
        $('#storageModal').modal('show');
    }

    // Attach the toggle function to the AuthenticationMethod dropdown
    $('#authMethod').change(toggleAuthFields);
});
