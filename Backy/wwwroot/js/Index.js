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

function showLoading() {
    const spinner = document.getElementById("loading-spinner");
    spinner.style.display = "flex";
    spinner.style.opacity = "1";  // Fade in
}

function hideLoading() {
    const spinner = document.getElementById("loading-spinner");
    spinner.style.opacity = "0";  // Fade out
    setTimeout(() => { spinner.style.display = "none"; }, 20);  // Hide after fade out
}

function showError(message) {
    const toast = document.getElementById("error-toast");
    const errorMessage = document.getElementById("error-message");

    if (errorMessage) {
        errorMessage.textContent = message;
    }

    if (toast) {
        const bootstrapToast = new bootstrap.Toast(toast);
        bootstrapToast.show();
    } else {
        console.error("Error toast element not found.");
    }
}

function submitForm(action, uuid) {
    // Create a form element
    const form = document.createElement('form');
    form.method = 'post';
    form.action = `?handler=${action}&uuid=${uuid}`;

    // Append form to the body and submit
    document.body.appendChild(form);
    form.submit();
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

