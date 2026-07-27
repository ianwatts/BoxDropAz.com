(function () {
    'use strict';

    // Bootstrap client-side validation styling for forms opting in with .needs-validation
    document.querySelectorAll('.needs-validation').forEach(function (form) {
        form.addEventListener('submit', function (event) {
            if (!form.checkValidity()) {
                event.preventDefault();
                event.stopPropagation();
            }
            form.classList.add('was-validated');
        }, false);
    });

    // Guard against double submission on payment and booking forms, where a second POST
    // could create a duplicate order.
    document.querySelectorAll('form[data-submit-once]').forEach(function (form) {
        form.addEventListener('submit', function () {
            var button = form.querySelector('button[type="submit"]');
            if (!button || button.dataset.submitting === '1') {
                return;
            }
            button.dataset.submitting = '1';
            button.disabled = true;
            button.dataset.originalText = button.innerHTML;
            button.innerHTML = '<span class="spinner-border spinner-border-sm me-2"></span>Working...';
        });
    });

    // Auto-dismiss transient alerts so a success banner doesn't linger over the page
    document.querySelectorAll('[data-auto-dismiss]').forEach(function (alert) {
        setTimeout(function () {
            alert.classList.remove('show');
        }, 6000);
    });
})();
