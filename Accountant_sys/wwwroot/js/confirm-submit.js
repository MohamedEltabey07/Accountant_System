(function () {
    'use strict';

    window.confirmSubmit = function (form, title, text, confirmText, confirmColor) {
        if (!form) return;
        if (window.jQuery && window.jQuery.validation && !form.checkValidity && window.jQuery(form).valid && !window.jQuery(form).valid()) {
            return;
        }
        Swal.fire({
            title: title || 'تأكيد',
            text: text || 'هل أنت متأكد من إجراء هذه العملية؟',
            icon: 'warning',
            showCancelButton: true,
            confirmButtonText: confirmText || 'نعم، متابعة',
            cancelButtonText: 'إلغاء',
            confirmButtonColor: confirmColor || '#0f3460'
        }).then(function (result) {
            if (result.isConfirmed) {
                form.submit();
            }
        });
    };

    document.addEventListener('DOMContentLoaded', function () {
        document.querySelectorAll('form[data-swal]').forEach(function (form) {
            form.addEventListener('submit', function (e) {
                e.preventDefault();
                e.stopPropagation();
                confirmSubmit(
                    form,
                    form.getAttribute('data-swal-title'),
                    form.getAttribute('data-swal-text'),
                    form.getAttribute('data-swal-confirm'),
                    form.getAttribute('data-swal-color')
                );
            });
        });
    });
})();
