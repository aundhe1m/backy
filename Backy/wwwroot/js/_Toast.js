function showToast(message, isSuccess, reload = false) {
    if (reload) {
        // Store the message and status in sessionStorage
        sessionStorage.setItem('toastMessage', JSON.stringify({ message, isSuccess }));
        // Immediately reload the page
        location.reload();
        return; // Do not proceed to show the toast on the current page
    }

    // Create or select the toast container
    const toastContainer = document.querySelector('.toast-container') || createToastContainer();

    // Create the toast element
    const toastElement = document.createElement('div');
    toastElement.classList.add('toast', 'align-items-center', 'text-white', 'border-0');
    toastElement.setAttribute('role', 'alert');
    toastElement.setAttribute('aria-live', 'assertive');
    toastElement.setAttribute('aria-atomic', 'true');

    // Add appropriate background based on success status
    toastElement.classList.add(isSuccess ? 'bg-success' : 'bg-danger');

    // Toast HTML structure
    toastElement.innerHTML = `
        <div class="d-flex">
            <div class="toast-body">
                ${message}
            </div>
            <button type="button" class="btn-close btn-close-white me-2 m-auto" data-bs-dismiss="toast" aria-label="Close"></button>
        </div>
    `;

    // Append the toast to the container
    toastContainer.appendChild(toastElement);

    // Initialize and show the toast
    const toast = new bootstrap.Toast(toastElement, { delay: 10000 });
    toast.show();

    // If the toast indicates a failure, log the error
    if (!isSuccess) {
        console.error(message);
    }
}

function createToastContainer() {
    const container = document.createElement('div');
    container.classList.add('toast-container', 'position-fixed', 'bottom-0', 'end-0', 'p-3');
    document.body.appendChild(container);
    return container;
}

// Handle displaying toast after page reload based on sessionStorage flag
document.addEventListener('DOMContentLoaded', function () {
    const toastData = sessionStorage.getItem('toastMessage');
    if (toastData) {
        const { message, isSuccess } = JSON.parse(toastData);
        // Display the toast without reloading
        showToast(message, isSuccess);
        // Remove the toast message from sessionStorage
        sessionStorage.removeItem('toastMessage');
    }
});