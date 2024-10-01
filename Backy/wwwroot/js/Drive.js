document.addEventListener('DOMContentLoaded', function () {
    // Variables to store selected drives for pool creation
    let selectedDrives = [];

    // Handle New Drive Cards
    const newDriveCards = document.querySelectorAll('.new-drive-card');
    const renamePoolForms = document.querySelectorAll('.rename-pool-form');

    // Handle Eject/Mount/Inspect actions for Pools
    const ejectPoolButtons = document.querySelectorAll('.eject-pool-button');
    const mountPoolButtons = document.querySelectorAll('.mount-pool-button');
    const inspectPoolButtons = document.querySelectorAll('.inspect-pool-button');

    // Handle Protected Drive Cards
    const protectedDriveCards = document.querySelectorAll('.protected-drive-card');

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
            });
    });

    // Handle Rename Pool
    renamePoolForms.forEach(form => {
        form.addEventListener('submit', function (e) {
            e.preventDefault();

            const poolGroupId = form.getAttribute('data-pool-group-id');
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
                'PoolGroupId': poolGroupId,
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

    ejectPoolButtons.forEach(button => {
        button.addEventListener('click', function () {
            const poolGroupId = button.getAttribute('data-pool-group-id');
            unmountPool(poolGroupId);
        });
    });

    mountPoolButtons.forEach(button => {
        button.addEventListener('click', function () {
            const poolGroupId = button.getAttribute('data-pool-group-id');
            mountPool(poolGroupId);
        });
    });

    inspectPoolButtons.forEach(button => {
        button.addEventListener('click', function () {
            const poolGroupId = button.getAttribute('data-pool-group-id');
            inspectPool(poolGroupId);
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
            modalFooter.innerHTML = '<button type="button" class="btn btn-secondary" data-bs-dismiss="modal">Close</button>';
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
    function unmountPool(poolGroupId) {
        fetch(`/Drive?handler=UnmountPool&poolGroupId=${poolGroupId}`, {
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
                    showProcessModal(poolGroupId, data.processes);
                } else {
                    showToast(`Failed to unmount pool: ${data.message}`, false);
                }
            })
            .catch(error => {
                showToast(`Error unmounting pool: ${error}`, false);
            });
    }

    function showProcessModal(poolGroupId, processes) {
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
            killProcesses(poolGroupId, processes);
            modal.hide();
        };

        modal.show();
    }

    // Function to kill processes
    function killProcesses(poolGroupId, processes) {
        const pids = processes.map(p => p.pid);
        fetch(`/Drive?handler=KillProcesses`, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'X-Requested-With': 'XMLHttpRequest'
            },
            body: JSON.stringify({ poolGroupId: poolGroupId, pids: pids })
        })
            .then(response => {
                if (!response.ok) {
                    return response.json().then(json => { throw json; });
                }
                return response.json();
            })
            .then(data => {
                if (data.success) {
                    showToast(`Processes killed and pool unmounted successfully.`, true, true);
                } else {
                    showToast(`Failed to kill processes: ${data.message}`, false);
                }
            })
            .catch(error => {
                showToast(`Error killing processes: ${error.message || 'Unknown error.'}`, false);
            });
    }

    function mountPool(poolGroupId) {
        fetch(`/Drive?handler=MountPool&poolGroupId=${poolGroupId}`, {
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

    function inspectPool(poolGroupId) {
        fetch(`/Drive?handler=InspectPool&poolGroupId=${poolGroupId}`, {
            method: 'POST',
            headers: {
                'X-Requested-With': 'XMLHttpRequest'
            }
        })
            .then(response => response.json())
            .then(data => {
                if (data.success) {
                    showInspectModal(data.output);
                } else {
                    showToast(`Failed to inspect pool: ${data.message}`, false);
                }
            })
            .catch(error => {
                showToast(`Error inspecting pool: ${error}`, false);
            });
    }

    function showInspectModal(output) {
        // Create and show a modal to display the output
        const inspectModalHtml = `
            <div class="modal fade" id="inspectModal" tabindex="-1" aria-labelledby="inspectModalLabel" aria-hidden="true">
                <div class="modal-dialog modal-lg">
                    <div class="modal-content">
                        <div class="modal-header">
                            <h5 class="modal-title" id="inspectModalLabel">Pool Inspection</h5>
                            <button type="button" class="btn-close" data-bs-dismiss="modal" aria-label="Close"></button>
                        </div>
                        <div class="modal-body">
                            <pre class="command-output">${output}</pre>
                        </div>
                    </div>
                </div>
            </div>
        `;

        document.body.insertAdjacentHTML('beforeend', inspectModalHtml);
        const inspectModal = new bootstrap.Modal(document.getElementById('inspectModal'));
        inspectModal.show();

        // Remove the modal from DOM after it's closed
        document.getElementById('inspectModal').addEventListener('hidden.bs.modal', function () {
            document.getElementById('inspectModal').remove();
        });
    }
});
