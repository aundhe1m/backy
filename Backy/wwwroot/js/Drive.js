document.addEventListener('DOMContentLoaded', function () {
    // Variables to store selected drives for pool creation
    let selectedDrives = [];

    let poolGroupGuidToRemove = null;

    // Handle New Drive Cards
    const newDriveCards = document.querySelectorAll('.new-drive-card');
    const renamePoolForms = document.querySelectorAll('.rename-pool-form');

    // Handle Eject/Mount/Status/Remove actions for Pools
    const ejectPoolButtons = document.querySelectorAll('.eject-pool-button');
    const mountPoolButtons = document.querySelectorAll('.mount-pool-button');
    const statusPoolButtons = document.querySelectorAll('.status-pool-button');
    const removePoolGroupButtons = document.querySelectorAll('.remove-pool-group-button');
    const forceAddButtons = document.querySelectorAll('.force-add-button');

    // Select all elements with the 'format-size' class
    const sizeElements = document.querySelectorAll('.format-size');

    // Search Functionality
    const searchInput = document.getElementById('driveSearchInput');

    // Handle Protected Drive Cards
    const protectedDriveCards = document.querySelectorAll('.protected-drive-card');

    sizeElements.forEach(function (element) {
        // Retrieve the raw size in bytes from the data attribute
        const sizeInBytes = parseInt(element.getAttribute('data-size'), 10);

        // Handle cases where sizeInBytes might not be a valid number
        if (!isNaN(sizeInBytes)) {
            // Use the formatSize function to get a human-readable format
            const formattedSize = formatSize(sizeInBytes);

            // Update the inner text of the span with the formatted size
            element.textContent = formattedSize;
        } else {
            // Fallback in case of invalid size
            element.textContent = '0 B';
        }
    });

    // Handle Create Pool button in toast
    document.getElementById('createPoolToastButton').addEventListener('click', function () {
        // Open the Create Pool modal
        const createPoolModal = new bootstrap.Modal(document.getElementById('createPoolModal'));
        createPoolModal.show();
    });

    // Handle Abort button
    document.getElementById('abortSelectionButton').addEventListener('click', function () {
        // Deselect all selected drives
        resetSelectedDrives();
        // Hide the toast
        const toastElement = document.getElementById('driveToast');
        const toast = bootstrap.Toast.getInstance(toastElement);
        toast.hide();
    });


    // Handle Create Pool form submission
    document.getElementById('createPoolForm').addEventListener('submit', function (e) {
        e.preventDefault();

        const poolLabel = document.getElementById('poolLabelInput').value.trim();
        if (!poolLabel) {
            showToast("Please provide a pool label.", false);
            return;
        }

        if (selectedDrives.length === 0) {
            showToast("No drives selected.", false);
            return;
        }

        // Collect drive labels
        const driveLabelsInputs = document.querySelectorAll('.drive-label-input');
        const driveLabels = {};
        selectedDrives.forEach((drive, index) => {
            const input = Array.from(driveLabelsInputs).find(input => input.getAttribute('data-drive-serial') === drive.driveSerial);
            if (input) {
                const label = input.value.trim();
                driveLabels[drive.driveSerial] = label;
            }
        });

        const driveSerials = selectedDrives.map(drive => drive.driveSerial);

        const postData = {
            PoolLabel: poolLabel,
            DriveSerials: driveSerials,
            DriveLabels: driveLabels
        };

        // Disable form elements
        document.getElementById('createPoolForm').querySelectorAll('input, button').forEach(el => el.disabled = true);

        // Show spinner
        showSpinner();

        // Send JSON to backend via fetch
        fetch('/Drive?handler=CreatePool', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'Accept': 'application/json',
                'X-Requested-With': 'XMLHttpRequest'
            },
            body: JSON.stringify(postData)
        })
            .then(response => {
                if (!response.ok) {
                    return response.json().then(json => { throw json; });
                }
                return response.json();
            })
            .then(data => {
                hideSpinner();
                displayCommandOutputs(data.outputs);
                updateModalFooter('success');

                document.getElementById('continueButton').addEventListener('click', function () {
                    showToast(`Pool created successfully.`, true, true);
                });

            })
            .catch(error => {
                hideSpinner();
                displayCommandOutputs(error.outputs || [], true);
                updateModalFooter('error');
                showToast(`Error creating pool: ${error.message || 'Unknown error.'}`, false);

                document.getElementById('closeButton').addEventListener('click', function () {
                    location.reload();
                });
            });
    });

    // Handle Rename Pool
    renamePoolForms.forEach(form => {
        form.addEventListener('submit', function (e) {
            e.preventDefault();

            const poolGroupGuid = form.getAttribute('data-pool-group-guid');
            const newPoolLabelInput = form.querySelector(`input[name="newPoolLabel"]`);
            const newPoolLabel = newPoolLabelInput.value.trim();

            if (!newPoolLabel) {
                showToast("Pool label cannot be empty.", false);
                return;
            }

            // Collect new drive labels
            const driveLabelInputs = form.querySelectorAll('.drive-new-label-input');
            const driveLabels = {};
            driveLabelInputs.forEach(input => {
                const driveId = parseInt(input.getAttribute('data-drive-id'));
                const label = input.value.trim();
                driveLabels[driveId] = label; // Empty string indicates no change
            });

            const postData = new URLSearchParams({
                'PoolGroupGuid': poolGroupGuid,
                'NewPoolLabel': newPoolLabel,
                'DriveLabels': JSON.stringify(driveLabels)
            });

            // Disable form elements to prevent multiple submissions
            form.querySelectorAll('input, button').forEach(el => el.disabled = true);

            // Show spinner
            showSpinner(form);

            // Send AJAX request to backend
            fetch('/Drive?handler=RenamePoolGroup', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/x-www-form-urlencoded',
                    'X-Requested-With': 'XMLHttpRequest'
                },
                body: postData.toString()
            })
                .then(response => {
                    if (!response.ok) {
                        return response.json().then(json => { throw json; });
                    }
                    return response.json();
                })
                .then(data => {
                    hideSpinner(form);

                    if (data.success) {
                        showToast(data.message, true, true);
                    } else {
                        showToast(data.message, false);
                        // Re-enable form elements
                        form.querySelectorAll('input, button').forEach(el => el.disabled = false);
                    }
                })
                .catch(error => {
                    hideSpinner(form);
                    showToast(error.message || "An error occurred while renaming the pool.", false);
                    // Re-enable form elements
                    form.querySelectorAll('input, button').forEach(el => el.disabled = false);
                });
        });
    });

    searchInput.addEventListener('input', function () {
        const query = this.value.trim().toLowerCase();
        const searchElements = document.querySelectorAll('[data-search-text]');

        searchElements.forEach(function (element) {
            const searchText = element.getAttribute('data-search-text').toLowerCase();
            const matches = searchText.includes(query);

            if (matches) {
                element.style.display = ''; // Show element
            } else {
                element.style.display = 'none'; // Hide element
            }
        });
    });

    // Handle Remove Pool Group button
    removePoolGroupButtons.forEach(button => {
        button.addEventListener('click', function () {
            poolGroupGuidToRemove = this.getAttribute('data-pool-group-guid');
            const removeModal = new bootstrap.Modal(document.getElementById('removePoolGroupModal'));
            removeModal.show();
        });
    });

    forceAddButtons.forEach(button => {
        button.addEventListener('click', function () {
            const driveId = this.getAttribute('data-drive-id');
            const poolGroupGuid = this.getAttribute('data-pool-group-guid');
            const devPath = this.getAttribute('data-dev-path');
            forceAddDrive(driveId, poolGroupGuid, devPath);
        });
    });

    function forceAddDrive(driveId, poolGroupGuid, devPath) {
        const postData = new URLSearchParams({
            'driveId': driveId,
            'poolGroupGuid': poolGroupGuid,
            'devPath': devPath
        });

        fetch('/Drive?handler=ForceAddDrive', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/x-www-form-urlencoded',
                'X-Requested-With': 'XMLHttpRequest'
            },
            body: postData.toString()
        })
            .then(response => {
                if (!response.ok) {
                    return response.json().then(json => { throw json; });
                }
                return response.json();
            })
            .then(data => {
                if (data.success) {
                    showToast(data.message, true, true);
                } else {
                    showToast(`Failed to force add drive: ${data.message}`, false);
                }
            })
            .catch(error => {
                showToast(`Error force adding drive: ${error.message || error}`, false);
            });
    }

    // Handle confirmation of removal
    document.getElementById('confirmRemovePoolGroupButton').addEventListener('click', function () {
        if (!poolGroupGuidToRemove) {
            showToast('Invalid Pool Group ID.', false);
            return;
        }

        // Disable the button to prevent multiple clicks
        this.disabled = true;

        // Send AJAX POST to RemovePoolGroup
        fetch(`/Drive?handler=RemovePoolGroup&poolGroupGuid=${poolGroupGuidToRemove}`, {
            method: 'POST',
            headers: {
                'X-Requested-With': 'XMLHttpRequest'
            }
        })
            .then(response => {
                if (!response.ok) {
                    return response.json().then(json => { throw json; });
                }
                return response.json();
            })
            .then(data => {
                // Hide the modal
                const removeModal = bootstrap.Modal.getInstance(document.getElementById('removePoolGroupModal'));
                removeModal.hide();

                if (data.success) {
                    showToast(data.message, true, true);
                    // Remove the Pool Group card from the UI
                    const poolGroupCard = document.querySelector(`.card[data-pool-group-guid="${poolGroupGuidToRemove}"]`);
                    if (poolGroupCard) {
                        poolGroupCard.remove();
                    }
                } else if (data.processes && data.processes.length > 0) {
                    // Show the processListModal with processes that are keeping the mount point busy
                    showProcessModal(poolGroupGuidToRemove, data.processes, 'RemovePoolGroup');
                } else {
                    showToast(`Failed to remove pool group: ${data.message}`, false);
                }
            })
            .catch(error => {
                // Hide the modal
                const removeModal = bootstrap.Modal.getInstance(document.getElementById('removePoolGroupModal'));
                removeModal.hide();

                if (error.message) {
                    showToast(`Error removing pool group: ${error.message}`, false);
                } else {
                    showToast(`Error removing pool group.`, false);
                }
            })
            .finally(() => {
                // Re-enable the button
                this.disabled = false;
                poolGroupGuidToRemove = null;
            });
    });

    ejectPoolButtons.forEach(button => {
        button.addEventListener('click', function () {
            const poolGroupGuid = button.getAttribute('data-pool-group-guid');
            unmountPool(poolGroupGuid);
        });
    });

    mountPoolButtons.forEach(button => {
        button.addEventListener('click', function () {
            const poolGroupGuid = button.getAttribute('data-pool-group-guid');
            mountPool(poolGroupGuid);
        });
    });

    statusPoolButtons.forEach(button => {
        button.addEventListener('click', function () {
            const poolGroupGuid = button.getAttribute('data-pool-group-guid');
            statusPool(poolGroupGuid);
        });
    });

    newDriveCards.forEach(card => {
        const selectButton = card.querySelector('.select-drive-button');
        const protectButton = card.querySelector('.protect-drive-button');
        const driveSerial = card.getAttribute('data-drive-serial');
        const driveData = {
            driveSerial: driveSerial,
            vendor: card.getAttribute('data-vendor'),
            model: card.getAttribute('data-model'),
            serial: card.getAttribute('data-serial')
        };

        // Handle select drive button click
        selectButton.addEventListener('click', function () {
            toggleDriveSelection(driveData);
        });

        // Handle protect drive button click
        protectButton.addEventListener('click', function () {
            protectDrive(driveData.serial);
        });
    });

    protectedDriveCards.forEach(card => {
        const unprotectButton = card.querySelector('.unprotect-drive-button');
        const serial = card.getAttribute('data-drive-serial');

        // Handle unprotect drive button click
        unprotectButton.addEventListener('click', function () {
            unprotectDrive(serial);
        });
    });

    // Function to toggle drive selection
    function toggleDriveSelection(driveData) {
        const existingDriveIndex = selectedDrives.findIndex(d => d.driveSerial === driveData.driveSerial);
        const driveCard = document.querySelector(`.new-drive-card[data-drive-serial="${driveData.driveSerial}"]`);
        const selectButtonImg = driveCard.querySelector('.select-drive-button img');

        if (existingDriveIndex !== -1) {
            // Drive is already selected; deselect it
            selectedDrives.splice(existingDriveIndex, 1);

            // Reset the icon to 'plus-square.svg'
            if (selectButtonImg) {
                selectButtonImg.src = '/icons/plus-square.svg';
            }

            // Show a toast indicating deselection
            showToast(`Drive deselected.`, true);
        } else {
            // Drive is not selected; select it
            selectedDrives.push(driveData);

            // Change the icon to 'plus-square-fill.svg' to indicate selection
            if (selectButtonImg) {
                selectButtonImg.src = '/icons/plus-square-fill.svg';
            }

            // Show the 'driveToast' only if it's not already visible
            const toastElement = document.getElementById('driveToast');
            if (!toastElement.classList.contains('show')) {
                const toast = new bootstrap.Toast(toastElement);
                toast.show();
            }
        }
        populateSelectedDrivesTable();
    }

    // Function to populate the table in the Create Pool modal
    function populateSelectedDrivesTable() {
        const selectedDrivesTable = document.getElementById('selectedDrivesTable').querySelector('tbody');
        selectedDrivesTable.innerHTML = ''; // Clear existing rows

        if (selectedDrives.length > 0) {
            selectedDrives.forEach((drive, index) => {
                const row = `<tr>
                                <td>${index + 1}</td>
                                <td>
                                    <input type="text" class="form-control drive-label-input" data-drive-serial="${drive.driveSerial}"
                                        placeholder="Optional">
                                </td>
                                <td>${drive.vendor}</td>
                                <td>${drive.model}</td>
                                <td>${drive.serial}</td>
                                
                             </tr>`;
                selectedDrivesTable.insertAdjacentHTML('beforeend', row);
            });
        } else {
            const emptyRow = `<tr><td colspan="6" class="text-center">No drives selected</td></tr>`;
            selectedDrivesTable.insertAdjacentHTML('beforeend', emptyRow);
        }
    }

    // Function to protect a drive
    function protectDrive(serial) {
        fetch(`/Drive?handler=ProtectDrive&serial=${serial}`, {
            method: 'POST',
            headers: {
                'X-Requested-With': 'XMLHttpRequest'
            }
        })
            .then(response => response.json())
            .then(data => {
                if (data.success) {
                    showToast(`Drive protected successfully.`, true, true);
                } else {
                    showToast(`Failed to protect drive: ${data.message}`, false);
                }
            })
            .catch(error => {
                showToast(`Error protecting drive: ${error}`, false);
            });
    }

    // Function to unprotect a drive
    function unprotectDrive(serial) {
        fetch(`/Drive?handler=UnprotectDrive&serial=${serial}`, {
            method: 'POST',
            headers: {
                'X-Requested-With': 'XMLHttpRequest'
            }
        })
            .then(response => response.json())
            .then(data => {
                if (data.success) {
                    showToast(`Drive unprotected successfully.`, true, true);
                } else {
                    showToast(`Failed to unprotect drive: ${data.message}`, false);
                }
            })
            .catch(error => {
                showToast(`Error unprotecting drive: ${error}`, false);
            });
    }

    // Function to reset selected drives and icons
    function resetSelectedDrives() {
        selectedDrives.forEach(function (drive) {
            const selectButtonImg = document.querySelector(`.new-drive-card[data-drive-serial="${drive.driveSerial}"] .select-drive-button img`);
            if (selectButtonImg) {
                selectButtonImg.src = '/icons/plus-square.svg';
            }
        });
        selectedDrives = [];
    }

    function showSpinner() {
        const spinner = document.createElement('div');
        spinner.classList.add('spinner-border', 'text-primary');
        spinner.role = 'status';
        spinner.innerHTML = '<span class="visually-hidden">Loading...</span>';
        document.querySelector('#createPoolModal .modal-body').appendChild(spinner);
    }

    function hideSpinner() {
        const spinner = document.querySelector('#createPoolModal .modal-body .spinner-border');
        if (spinner) {
            spinner.remove();
        }
    }

    function updateModalFooter(status) {
        const modalFooter = document.querySelector('#createPoolModal .modal-footer');
        if (status === 'success') {
            modalFooter.innerHTML = '<button type="button" class="btn btn-primary" id="continueButton">Continue</button>';
        } else {
            modalFooter.innerHTML = '<button type="button" class="btn btn-secondary" id="closeButton">Close</button>';
        }
    }

    function displayCommandOutputs(outputs, isError = false) {
        const modalBody = document.querySelector('#createPoolModal .modal-body');
        const poolCreationForm = document.getElementById('poolCreationForm');
        const commandOutputs = document.getElementById('commandOutputs');
        const commandOutputPre = commandOutputs.querySelector('.command-output');

        // Hide the form and show the outputs
        poolCreationForm.style.display = 'none';
        commandOutputs.style.display = 'block';

        // Set the outputs
        commandOutputPre.textContent = outputs.join('\n');

        // If error, you can change the text color to red
        if (isError) {
            commandOutputPre.style.color = '#ff0000';
        }
    }

    // Functions to handle pool actions
    function unmountPool(poolGroupGuid) {
        fetch(`/Drive?handler=UnmountPool&poolGroupGuid=${poolGroupGuid}`, {
            method: 'POST',
            headers: {
                'X-Requested-With': 'XMLHttpRequest'
            }
        })
            .then(response => response.json())
            .then(data => {
                if (data.success) {
                    showToast(`Pool unmounted successfully.`, true, true);
                } else if (data.processes && data.processes.length > 0) {
                    showProcessModal(poolGroupGuid, data.processes, 'UnmountPool');
                } else {
                    showToast(`Failed to unmount pool: ${data.message}`, false);
                }
            })
            .catch(error => {
                showToast(`Error unmounting pool: ${error}`, false);
            });
    }


    function showProcessModal(poolGroupGuid, processes, action) {
        const modal = new bootstrap.Modal(document.getElementById('processListModal'));
        const tableBody = document.getElementById('processListTableBody');
        tableBody.innerHTML = ''; // Clear existing rows

        processes.forEach(process => {
            const row = `<tr>
                <td>${process.pid}</td>
                <td>${process.command}</td>
                <td>${process.user}</td>
                <td>${process.name}</td>
            </tr>`;
            tableBody.insertAdjacentHTML('beforeend', row);
        });

        document.getElementById('killProcessesButton').onclick = function () {
            killProcesses(poolGroupGuid, processes, action);
            modal.hide();
        };

        modal.show();
    }

    // Function to kill processes
    function killProcesses(poolGroupGuid, processes, action) {
        const pids = processes.map(p => p.pid);
        const postData = {
            poolGroupGuid: poolGroupGuid,
            pids: pids,
            action: action
        };
        fetch(`/Drive?handler=KillProcesses`, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'X-Requested-With': 'XMLHttpRequest'
            },
            body: JSON.stringify(postData)
        })
            .then(response => {
                if (!response.ok) {
                    return response.json().then(json => { throw json; });
                }
                return response.json();
            })
            .then(data => {
                if (data.success) {
                    showToast(data.message, true, true);
                    // Remove the Pool Group card from the UI if action is RemovePoolGroup
                    if (action === 'RemovePoolGroup') {
                        const poolGroupCard = document.querySelector(`.card[data-pool-group-guid="${poolGroupGuid}"]`);
                        if (poolGroupCard) {
                            poolGroupCard.remove();
                        }
                    }
                } else {
                    showToast(`Failed to kill processes: ${data.message}`, false);
                }
            })
            .catch(error => {
                if (error.message) {
                    showToast(`Error killing processes: ${error.message}`, false);
                } else {
                    showToast(`Error killing processes.`, false);
                }
            });
    }


    function mountPool(poolGroupGuid) {
        fetch(`/Drive?handler=MountPool&poolGroupGuid=${poolGroupGuid}`, {
            method: 'POST',
            headers: {
                'X-Requested-With': 'XMLHttpRequest'
            }
        })
            .then(response => response.json())
            .then(data => {
                if (data.success) {
                    showToast(`Pool mounted successfully.`, true, true);
                } else {
                    showToast(`Failed to mount pool: ${data.message}`, false);
                }
            })
            .catch(error => {
                showToast(`Error mounting pool: ${error}`, false);
            });
    }

    function statusPool(poolGroupGuid) {
        fetch(`/Drive?handler=StatusPool&poolGroupGuid=${poolGroupGuid}`, {
            method: 'POST',
            headers: {
                'X-Requested-With': 'XMLHttpRequest'
            }
        })
            .then(response => response.json())
            .then(data => {
                if (data.success) {
                    showStatusModal(data.output);
                } else {
                    showToast(`Failed to status pool: ${data.message}`, false);
                }
            })
            .catch(error => {
                showToast(`Error statusing pool: ${error}`, false);
            });
    }

    function showStatusModal(output) {
        // Create and show a modal to display the output
        const statusModalHtml = `
            <div class="modal fade" id="statusModal" tabindex="-1" aria-labelledby="statusModalLabel" aria-hidden="true">
                <div class="modal-dialog modal-lg">
                    <div class="modal-content">
                        <div class="modal-header">
                            <h5 class="modal-title" id="statusModalLabel">Pool Statusion</h5>
                            <button type="button" class="btn-close" data-bs-dismiss="modal" aria-label="Close"></button>
                        </div>
                        <div class="modal-body">
                            <pre class="command-output">${output}</pre>
                        </div>
                    </div>
                </div>
            </div>
        `;

        document.body.insertAdjacentHTML('beforeend', statusModalHtml);
        const statusModal = new bootstrap.Modal(document.getElementById('statusModal'));
        statusModal.show();

        // Remove the modal from DOM after it's closed
        document.getElementById('statusModal').addEventListener('hidden.bs.modal', function () {
            document.getElementById('statusModal').remove();
        });
    }
});
