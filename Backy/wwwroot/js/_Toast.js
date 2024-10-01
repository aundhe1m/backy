function showToast(message, isSuccess, reload = false) {
    // If the toast requires a reload, store the message and status in sessionStorage
    if (reload) {
        sessionStorage.setItem('toastMessage', JSON.stringify({ message, isSuccess }));
    }

    // Create or select the toast container
    const toastContainer = document.querySelector('.toast-container') || createToastContainer();

    // Create the toast element
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

    // Append the toast to the container
    toastContainer.appendChild(toastElement);

    // Initialize and show the toast
    const toast = new bootstrap.Toast(toastElement, { delay: 3000 }); // 3-second delay
    toast.show();

    // Automatically reload the page after the toast is hidden if required
    if (reload) {
        toastElement.addEventListener('hidden.bs.toast', function () {
            location.reload();
        });
    }

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
        showToast(message, isSuccess);
        sessionStorage.removeItem('toastMessage');
    }
});