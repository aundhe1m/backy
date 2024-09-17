// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.

function showLoading() {
    const spinner = document.getElementById("loading-spinner");
    spinner.style.display = "flex";
}

function hideLoading() {
    const spinner = document.getElementById("loading-spinner");
    spinner.style.display = "none";
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

function showSuccess(message) {
    const toast = document.getElementById("success-toast");
    const successMessage = document.getElementById("success-message");

    if (successMessage) {
        successMessage.textContent = message;
    }

    if (toast) {
        const bootstrapToast = new bootstrap.Toast(toast);
        bootstrapToast.show();
    } else {
        console.success("Success toast element not found.");
    }
}