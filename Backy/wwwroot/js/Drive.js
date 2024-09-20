// Function to format size in bytes to human-readable format
function formatSize(sizeInBytes) {
    if (sizeInBytes === null || sizeInBytes === 0) return 'Unknown size';

    let size = sizeInBytes;
    const suffixes = ['B', 'KB', 'MB', 'GB', 'TB'];
    let suffixIndex = 0;

    while (size >= 1024 && suffixIndex < suffixes.length - 1) {
        size /= 1024;
        suffixIndex++;
    }

    return size.toFixed(2) + ' ' + suffixes[suffixIndex];
}

// Initialize tooltips
document.addEventListener('DOMContentLoaded', function () {
    var tooltipTriggerList = [].slice.call(document.querySelectorAll('[data-bs-toggle="tooltip"]'));
    tooltipTriggerList.map(function (tooltipTriggerEl) {
        return new bootstrap.Tooltip(tooltipTriggerEl);
    });
});

// Variables to store selected drives for pool creation
let selectedDrives = [];

// Handle split buttons in NewDrive cards
document.addEventListener('DOMContentLoaded', function () {
    const driveCards = document.querySelectorAll('.new-drive-card');

    driveCards.forEach(card => {
        const selectButton = card.querySelector('.select-drive-button');
        const wipeButton = card.querySelector('.wipe-drive-button');

        // Handle select drive button click
        selectButton.addEventListener('click', function () {
            const driveName = card.getAttribute('data-drive-name');
            const driveData = {
                driveName: driveName,
                vendor: card.getAttribute('data-vendor'),
                model: card.getAttribute('data-model'),
                serial: card.getAttribute('data-serial')
            };
            // Add drive to selected drives list
            addDriveToPool(driveData);
        });

        // Handle wipe drive button click
        wipeButton.addEventListener('click', function () {
            const driveName = card.getAttribute('data-drive-name');
            if (confirm(`Are you sure you want to wipe drive ${driveName}? This will remove all partitions.`)) {
                wipeDrive(driveName);
            }
        });
    });

    // Function to add drive to pool selection
    function addDriveToPool(driveData) {
        // Check if drive is already selected
        if (selectedDrives.find(d => d.driveName === driveData.driveName)) {
            alert("Drive is already selected.");
            return;
        }

        selectedDrives.push(driveData);

        // Change the icon to 'plus-square-fill.svg' to indicate selection
        const selectButtonImg = document.querySelector(`.new-drive-card[data-drive-name="${driveData.driveName}"] .select-drive-button img`);
        if (selectButtonImg) {
            selectButtonImg.src = '/icons/plus-square-fill.svg';
        }

        // Show the toast notification
        const toastElement = document.getElementById('driveToast');
        const toast = new bootstrap.Toast(toastElement);
        toast.show();

        // Update the selected drives table
        populateSelectedDrivesTable();
    }

    // Function to reset selected drives and icons
    function resetSelectedDrives() {
        // Reset icons back to 'plus-square.svg'
        selectedDrives.forEach(function (drive) {
            const selectButtonImg = document.querySelector(`.new-drive-card[data-drive-name="${drive.driveName}"] .select-drive-button img`);
            if (selectButtonImg) {
                selectButtonImg.src = '/icons/plus-square.svg';
            }
        });
        selectedDrives = [];
    }

    // Function to populate the table in the Create Pool modal
    function populateSelectedDrivesTable() {
        const selectedDrivesTable = document.getElementById('selectedDrivesTable').querySelector('tbody');
        selectedDrivesTable.innerHTML = ''; // Clear existing rows

        if (selectedDrives.length > 0) {
            selectedDrives.forEach((drive, index) => {
                const row = `<tr>
                                <td>${index + 1}</td>
                                <td>${drive.driveName}</td>
                                <td>${drive.vendor}</td>
                                <td>${drive.model}</td>
                                <td>${drive.serial}</td>
                             </tr>`;
                selectedDrivesTable.insertAdjacentHTML('beforeend', row);
            });
        } else {
            const emptyRow = `<tr><td colspan="5" class="text-center">No drives selected</td></tr>`;
            selectedDrivesTable.insertAdjacentHTML('beforeend', emptyRow);
        }
    }

    // Function to wipe a drive
    function wipeDrive(driveName) {
        fetch(`/Drive?handler=WipeDrive&driveName=${driveName}`, {
            method: 'POST',
            headers: {
                'X-Requested-With': 'XMLHttpRequest'
            }
        })
            .then(response => response.json())
            .then(data => {
                if (data.success) {
                    alert(`Drive ${driveName} wiped successfully.`);
                    location.reload();
                } else {
                    alert(`Failed to wipe drive ${driveName}: ${data.message}`);
                }
            })
            .catch(error => {
                console.error('Error wiping drive:', error);
                alert(`Error wiping drive ${driveName}: ${error}`);
            });
    }

    // Handle Create Pool button in toast
    document.getElementById('createPoolToastButton').addEventListener('click', function () {
        // Open the Create Pool modal
        const createPoolModal = new bootstrap.Modal(document.getElementById('createPoolModal'));
        createPoolModal.show();
    });

    // Handle Abort button
    document.getElementById('abortSelectionButton').addEventListener('click', function () {
        // Deselect all selected drives
        selectedDrives = [];
        // Hide the toast
        const toastElement = document.getElementById('driveToast');
        const toast = bootstrap.Toast.getInstance(toastElement);
        toast.hide();
    });

    // Handle Cancel button in Create Pool modal
    document.getElementById('cancelCreatePoolButton').addEventListener('click', function () {
        // Deselect all selected drives
        selectedDrives = [];
    });
});

// Handle Create Pool form submission
document.getElementById('createPoolForm').addEventListener('submit', function (e) {
    e.preventDefault();

    const poolLabel = document.getElementById('poolLabelInput').value;
    if (!poolLabel) {
        alert("Please provide a pool label.");
        return;
    }

    if (selectedDrives.length === 0) {
        alert("No drives selected.");
        return;
    }

    const driveNames = selectedDrives.map(drive => drive.driveName);

    const postData = {
        PoolLabel: poolLabel,
        DriveNames: driveNames
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
            // Hide spinner
            hideSpinner();

            // Display command outputs
            displayCommandOutputs(data.outputs);

            // Update modal footer
            updateModalFooter('success');

            // Handle 'Continue' button click
            document.getElementById('continueButton').addEventListener('click', function () {
                location.reload();
            });
        })
        .catch(error => {
            // Hide spinner
            hideSpinner();

            console.error("Error creating pool:", error);
            // Display error outputs
            displayCommandOutputs(error.outputs || [], true);

            // Update modal footer
            updateModalFooter('error');
        });
});

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

function updateModalFooter(status) {
    const modalFooter = document.querySelector('#createPoolModal .modal-footer');
    if (status === 'success') {
        modalFooter.innerHTML = '<button type="button" class="btn btn-primary" id="continueButton">Continue</button>';
    } else {
        modalFooter.innerHTML = '<button type="button" class="btn btn-secondary" data-bs-dismiss="modal">Close</button>';
    }
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


// Handle eject and mount actions in PoolGroup drive cards
document.addEventListener('DOMContentLoaded', function () {
    const ejectButtons = document.querySelectorAll('.eject-drive-button');
    const mountButtons = document.querySelectorAll('.mount-drive-button');

    ejectButtons.forEach(button => {
        button.addEventListener('click', function () {
            const partitionName = button.getAttribute('data-partition-name');
            unmountPartition(partitionName);
        });
    });

    mountButtons.forEach(button => {
        button.addEventListener('click', function () {
            const partitionName = button.getAttribute('data-partition-name');
            mountPartition(partitionName);
        });
    });

    function unmountPartition(partitionName) {
        // Disable button and show spinner
        const button = document.querySelector(`.eject-drive-button[data-partition-name="${partitionName}"]`);
        button.disabled = true;
        showSpinner();

        fetch(`/Drive?handler=UnmountPartition&partitionName=${partitionName}`, {
            method: 'POST',
            headers: {
                'X-Requested-With': 'XMLHttpRequest'
            }
        })
            .then(response => response.json())
            .then(data => {
                hideSpinner();
                if (data.success) {
                    showToast(`Partition ${partitionName} unmounted successfully.`, true);
                    location.reload();
                } else {
                    showToast(`Failed to unmount partition ${partitionName}: ${data.message}`, false);
                    button.disabled = false;
                }
            })
            .catch(error => {
                hideSpinner();
                console.error('Error unmounting partition:', error);
                showToast(`Error unmounting partition ${partitionName}: ${error}`, false);
                button.disabled = false;
            });
    }

    function mountPartition(partitionName) {
        // Disable button and show spinner
        const button = document.querySelector(`.mount-drive-button[data-partition-name="${partitionName}"]`);
        button.disabled = true;
        showSpinner();

        fetch(`/Drive?handler=MountPartition&partitionName=${partitionName}`, {
            method: 'POST',
            headers: {
                'X-Requested-With': 'XMLHttpRequest'
            }
        })
            .then(response => response.json())
            .then(data => {
                hideSpinner();
                if (data.success) {
                    showToast(`Partition ${partitionName} mounted successfully.`, true);
                    location.reload();
                } else {
                    showToast(`Failed to mount partition ${partitionName}: ${data.message}`, false);
                    button.disabled = false;
                }
            })
            .catch(error => {
                hideSpinner();
                console.error('Error mounting partition:', error);
                showToast(`Error mounting partition ${partitionName}: ${error}`, false);
                button.disabled = false;
            });
    }

    // Handle collapse chevron rotation
    document.querySelectorAll('[data-bs-toggle="collapse"]').forEach(function (toggleButton) {
        var targetId = toggleButton.getAttribute('data-bs-target');
        var collapseElement = document.querySelector(targetId);

        if (collapseElement && toggleButton) {
            // Initialize collapsed state
            toggleButton.classList.add('collapsed');

            collapseElement.addEventListener('shown.bs.collapse', function () {
                toggleButton.classList.remove('collapsed');
            });

            collapseElement.addEventListener('hidden.bs.collapse', function () {
                toggleButton.classList.add('collapsed');
            });
        }
    });
});



// Drive search functionality
document.getElementById('driveSearchInput').addEventListener('input', function () {
    const searchTerm = this.value.toLowerCase();
    document.querySelectorAll('.drive-card').forEach(function (card) {
        const text = card.getAttribute('data-search-text').toLowerCase();
        if (text.includes(searchTerm)) {
            card.style.display = '';
        } else {
            card.style.display = 'none';
        }
    });
});

function showToast(message, isSuccess) {
    const toastContainer = document.querySelector('.toast-container') || createToastContainer();
    const toastElement = document.createElement('div');
    toastElement.classList.add('toast', 'align-items-center', 'text-white', 'border-0');
    toastElement.role = 'alert';
    toastElement.ariaLive = 'assertive';
    toastElement.ariaAtomic = 'true';

    if (isSuccess) {
        toastElement.classList.add('bg-success');
    } else {
        toastElement.classList.add('bg-danger');
    }

    toastElement.innerHTML = `
        <div class="d-flex">
            <div class="toast-body">
                ${message}
            </div>
            <button type="button" class="btn-close btn-close-white me-2 m-auto" data-bs-dismiss="toast" aria-label="Close"></button>
        </div>
    `;

    toastContainer.appendChild(toastElement);
    const toast = new bootstrap.Toast(toastElement);
    toast.show();
}

function createToastContainer() {
    const container = document.createElement('div');
    container.classList.add('toast-container', 'position-fixed', 'bottom-0', 'end-0', 'p-3');
    document.body.appendChild(container);
    return container;
}