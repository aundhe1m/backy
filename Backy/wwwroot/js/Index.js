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

function submitForm(action, uuid) {
    const form = document.createElement('form');
    form.method = 'post';
    form.action = `?handler=${action}&uuid=${uuid}`;
    document.body.appendChild(form);
    form.submit();
}

function confirmRemove() {
    return confirm("Are you sure you want to remove this drive? This action cannot be undone.");
}

var tooltipTriggerList = [].slice.call(document.querySelectorAll('[data-bs-toggle="tooltip"]'));
var tooltipList = tooltipTriggerList.map(function (tooltipTriggerEl) {
    return new bootstrap.Tooltip(tooltipTriggerEl);
});

document.addEventListener('DOMContentLoaded', function () {
    document.querySelectorAll("form").forEach(form => {
        form.addEventListener("submit", function (e) {
            e.preventDefault();  // Prevent default form submission
            showLoading();  // Show spinner

            const formData = new FormData(form);
            const action = form.getAttribute("action");
            const method = form.getAttribute("method");

            // Perform the fetch request
            fetch(action, {
                method: method,
                body: new URLSearchParams(formData),
                headers: {
                    'X-Requested-With': 'XMLHttpRequest'
                }
            })
                .then(response => {
                    if (!response.ok) {
                        throw new Error('HTTP error! Status: ' + response.status);
                    }
                    return response.json();
                })
                .then(data => {
                    hideLoading();  // Hide spinner
                    if (data.success) {
                        console.log(data.message);
                        location.reload();  // Reload the page to reflect changes
                    } else {
                        throw new Error(data.message);
                    }
                })
                .catch(error => {
                    hideLoading();  // Hide spinner
                    showError(error.message);  // Show error in toast
                });
        });
    });
});



function submitToggleForm(uuid, isEnabled) {
    const form = document.getElementById(`toggleForm-${uuid}`);
    const formData = new FormData(form);
    formData.set('isEnabled', isEnabled);

    fetch(form.action, {
        method: 'POST',
        body: new URLSearchParams(formData),
        headers: {
            'X-Requested-With': 'XMLHttpRequest'  // Important to prevent full-page reload
        }
    })
        .then(response => response.json())
        .then(data => {
            if (data.success) {
                location.reload();
            } else {
                showError(data.message);
            }
        })
        .catch(error => {
            showError(error)
            console.error('Error:', error);
        });
}

document.addEventListener('DOMContentLoaded', function () {
    const checkboxes = document.querySelectorAll('.pool-check');
    const createPoolButton = document.getElementById('createPoolButton');
    const selectedDrivesTable = document.getElementById('selectedDrivesTable');
    let selectedDrives = [];

    // Detect drive checkbox changes
    checkboxes.forEach(checkbox => {
        checkbox.addEventListener('change', function () {
            console.log("Checkbox change detected for drive:", checkbox.value, "Checked:", checkbox.checked);

            if (checkbox.checked) {
                // Add drive to the selectedDrives array
                selectedDrives.push({
                    uuid: checkbox.value,
                    size: checkbox.getAttribute('data-size'),
                    vendor: checkbox.getAttribute('data-vendor'),
                    model: checkbox.getAttribute('data-model'),
                    serial: checkbox.getAttribute('data-serial')
                });
            } else {
                // Remove drive from the selectedDrives array
                selectedDrives = selectedDrives.filter(d => d.uuid !== checkbox.value);
            }

            // Populate the table in the modal when selected drives change
            populateSelectedDrivesTable();
        });
    });

    function populateSelectedDrivesTable() {
        selectedDrivesTable.innerHTML = ''; // Clear the existing table rows

        if (selectedDrives.length > 0) {
            selectedDrives.forEach((drive, index) => {
                const row = `<tr>
                                <td>${index + 1}</td>
                                <td>${formatSize(drive.size)}</td>
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

    // Handle Pool creation
    const createPoolSubmitButton = document.getElementById('createPoolSubmit');
    createPoolSubmitButton.addEventListener('click', function () {
        const poolLabel = document.getElementById('poolLabelInput').value;
        if (!poolLabel) {
            alert("Please provide a pool label.");
            return;
        }

        if (selectedDrives.length === 0) {
            alert("No drives selected.");
            return;
        }

        const postData = {
            PoolLabel: poolLabel,
            Uuids: selectedDrives.map(drive => drive.uuid)
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
});

document.addEventListener('DOMContentLoaded', function () {
    const checkboxes = document.querySelectorAll('.pool-check');
    const toastElement = document.getElementById('driveToast');
    const toast = new bootstrap.Toast(toastElement, { autohide: false });
    let selectedDrives = [];

    checkboxes.forEach(checkbox => {
        checkbox.addEventListener('change', function () {
            if (checkbox.checked) {
                selectedDrives.push({
                    uuid: checkbox.value,
                    size: checkbox.getAttribute('data-size'),
                    vendor: checkbox.getAttribute('data-vendor'),
                    model: checkbox.getAttribute('data-model'),
                    serial: checkbox.getAttribute('data-serial')
                });
                toast.show();
            } else {
                selectedDrives = selectedDrives.filter(d => d.uuid !== checkbox.value);
                if (selectedDrives.length === 0) {
                    toast.hide();
                }
            }
            // Update your table or any other UI elements as needed
        });
    });

    // Handle Create Pool button in toast
    document.getElementById('createPoolToastButton').addEventListener('click', function () {
        // Open the Create Pool modal
        $('#createPoolModal').modal('show');
        // Populate the table with selected drives
        populateSelectedDrivesTable();
    });

    // Handle Abort button
    document.getElementById('abortSelectionButton').addEventListener('click', function () {
        // Deselect all checkboxes
        checkboxes.forEach(checkbox => checkbox.checked = false);
        selectedDrives = [];
        toast.hide();
    });
});

document.querySelectorAll('[data-bs-toggle="collapse"]').forEach(function (toggleButton) {
    var chevron = toggleButton.querySelector('img');

    toggleButton.addEventListener('click', function () {
        var targetId = toggleButton.getAttribute('data-bs-target');
        var collapseElement = document.querySelector(targetId);

        collapseElement.addEventListener('shown.bs.collapse', function () {
            if (chevron) {
                chevron.src = '/icons/chevron-up.svg';
            }
        });
        collapseElement.addEventListener('hidden.bs.collapse', function () {
            if (chevron) {
                chevron.src = '/icons/chevron-down.svg';
            }
        });
    });
});

