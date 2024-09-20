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
var tooltipTriggerList = [].slice.call(document.querySelectorAll('[data-bs-toggle="tooltip"]'));
var tooltipList = tooltipTriggerList.map(function (tooltipTriggerEl) {
    return new bootstrap.Tooltip(tooltipTriggerEl);
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

        // Show the Create Pool modal if not already visible
        const createPoolModal = new bootstrap.Modal(document.getElementById('createPoolModal'));
        createPoolModal.show();

        // Update the selected drives table
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

    // Send JSON to backend via fetch
    fetch('/Drive?handler=CreatePool', {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json',
            'Accept': 'application/json'
        },
        body: JSON.stringify(postData)
    })
        .then(response => {
            if (!response.ok) {
                return response.text().then(text => { throw new Error(text); });
            }
            return response.json();
        })
        .then(data => {
            if (data.success) {
                alert(data.message);
                location.reload();
            } else {
                alert("Error: " + data.message);
            }
        })
        .catch(error => {
            console.error("Error creating pool:", error);
        });
});

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
        fetch(`/Drive?handler=UnmountPartition&partitionName=${partitionName}`, {
            method: 'POST',
            headers: {
                'X-Requested-With': 'XMLHttpRequest'
            }
        })
            .then(response => response.json())
            .then(data => {
                if (data.success) {
                    alert(`Partition ${partitionName} unmounted successfully.`);
                    location.reload();
                } else {
                    alert(`Failed to unmount partition ${partitionName}: ${data.message}`);
                }
            })
            .catch(error => {
                console.error('Error unmounting partition:', error);
                alert(`Error unmounting partition ${partitionName}: ${error}`);
            });
    }

    function mountPartition(partitionName) {
        fetch(`/Drive?handler=MountPartition&partitionName=${partitionName}`, {
            method: 'POST',
            headers: {
                'X-Requested-With': 'XMLHttpRequest'
            }
        })
            .then(response => response.json())
            .then(data => {
                if (data.success) {
                    alert(`Partition ${partitionName} mounted successfully.`);
                    location.reload();
                } else {
                    alert(`Failed to mount partition ${partitionName}: ${data.message}`);
                }
            })
            .catch(error => {
                console.error('Error mounting partition:', error);
                alert(`Error mounting partition ${partitionName}: ${error}`);
            });
    }
});

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

// Drive search functionality
document.getElementById('driveSearchInput').addEventListener('input', function () {
    const searchTerm = this.value.toLowerCase();
    document.querySelectorAll('.drive-card').forEach(function (card) {
        const text = card.innerText.toLowerCase();
        if (text.includes(searchTerm)) {
            card.style.display = '';
        } else {
            card.style.display = 'none';
        }
    });
});
