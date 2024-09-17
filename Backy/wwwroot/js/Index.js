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
