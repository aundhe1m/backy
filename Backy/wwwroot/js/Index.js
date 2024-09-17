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
    console.log("DOM fully loaded and parsed.");

    // Existing form submit handlers
    document.querySelectorAll("form").forEach(form => {
        form.addEventListener("submit", function (e) {
            e.preventDefault();  // Prevent default form submission
            console.log("Form submission intercepted for form:", form);

            showLoading();  // Show spinner
            const formData = new FormData(form);
            const action = form.getAttribute("action");
            const method = form.getAttribute("method");

            console.log("Submitting form data to action:", action, "with method:", method);

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
                    console.log("Form submission successful. Data:", data);

                    if (data.success) {
                        location.reload();  // Reload the page to reflect changes
                    } else {
                        throw new Error(data.message);
                    }
                })
                .catch(error => {
                    hideLoading();  // Hide spinner
                    showError(error.message);  // Show error in toast
                    console.error("Error during form submission:", error);
                });
        });
    });

    // Pool selection and table population logic
    const checkboxes = document.querySelectorAll('.pool-check');
    const createPoolButton = document.getElementById('createPoolButton');
    const selectedDrivesTable = document.getElementById('selectedDrivesTable');
    let selectedDrives = [];

    console.log("Found checkboxes for pool selection:", checkboxes.length);

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
                console.log("Drive added to pool:", checkbox.value);
            } else {
                // Remove drive from the selectedDrives array
                selectedDrives = selectedDrives.filter(d => d.uuid !== checkbox.value);
                console.log("Drive removed from pool:", checkbox.value);
            }

            console.log("Currently selected drives:", selectedDrives);

            // Enable or disable the "Create Pool" button based on selected drives
            createPoolButton.disabled = selectedDrives.length === 0;
            console.log("Create Pool button state:", createPoolButton.disabled);

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
                console.log("Drive row added to table:", row);
            });
        } else {
            const emptyRow = `<tr><td colspan="5" class="text-center">No drives selected</td></tr>`;
            selectedDrivesTable.insertAdjacentHTML('beforeend', emptyRow);
            console.log("No drives selected.");
        }
    }

    // Pool creation logic
    window.createPool = function () {
        const poolLabel = document.getElementById('poolLabelInput').value;
        console.log("Pool label:", poolLabel);

        if (!poolLabel) {
            alert("Please provide a pool label.");
            return;
        }

        if (selectedDrives.length === 0) {
            alert("No drives selected.");
            return;
        }

        console.log("Selected drives for pool creation:", selectedDrives);

        const postData = {
            PoolLabel: poolLabel,
            Uuids: selectedDrives.map(drive => drive.uuid)
        };

        console.log("Post data being sent:", JSON.stringify(postData));

        fetch('/Drive/CreatePool', {
            method: 'POST',
            body: JSON.stringify(postData),
            headers: {
                'Content-Type': 'application/json',
                'Accept': 'application/json'
            }
        })
            .then(response => {
                console.log("Response status:", response.status);
                return response.json();
            })
            .then(data => {
                if (data.success) {
                    alert(data.message);
                    location.reload(); // Refresh the page to reflect changes
                } else {
                    alert("Error: " + data.message);
                }
            })
            .catch(error => {
                console.error("Error creating pool:", error);
            });

    };


    // Utility function for formatting sizes
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
});
