/**
 * Accountant_sys - Universal Real-Time Table Filter & CSV Exporter
 * Ultra-lightweight Vanilla JS for instant search with zero server roundtrips.
 */

document.addEventListener('DOMContentLoaded', function () {
    // 1. Initialize all real-time table search inputs
    const searchInputs = document.querySelectorAll('[data-table-search]');

    searchInputs.forEach(input => {
        const targetTableId = input.getAttribute('data-table-search');
        const counterTargetId = input.getAttribute('data-counter-target');
        const table = document.getElementById(targetTableId);

        if (!table) return;

        const tbody = table.querySelector('tbody');
        if (!tbody) return;

        const allRows = Array.from(tbody.querySelectorAll('tr:not(.no-filter-row)'));
        const totalCount = allRows.length;

        function updateCounter(visibleCount) {
            if (counterTargetId) {
                const counterEl = document.getElementById(counterTargetId);
                if (counterEl) {
                    counterEl.textContent = `عرض ${visibleCount} من إجمالي ${totalCount} سجل`;
                }
            }
        }

        updateCounter(totalCount);

        input.addEventListener('input', function () {
            const query = this.value.trim().toLowerCase();
            let visibleCount = 0;

            allRows.forEach(row => {
                const text = row.textContent.toLowerCase();
                if (text.includes(query)) {
                    row.style.display = '';
                    visibleCount++;
                } else {
                    row.style.display = 'none';
                }
            });

            updateCounter(visibleCount);

            // Handle Empty Search Results State
            let emptyMsgRow = tbody.querySelector('.search-empty-row');
            if (visibleCount === 0 && query !== '') {
                if (!emptyMsgRow) {
                    emptyMsgRow = document.createElement('tr');
                    emptyMsgRow.className = 'search-empty-row';
                    const colCount = table.querySelectorAll('thead th').length || 6;
                    emptyMsgRow.innerHTML = `
                        <td colspan="${colCount}" class="text-center py-4 text-muted bg-light">
                            <i class="bi bi-search fs-2 d-block mb-2 text-secondary"></i>
                            <div class="fw-bold">لا توجد نتائج مطابقة للبحث: "${query}"</div>
                            <small class="text-muted">جرب البحث بكلمة أخرى أو رقم مستند مختلف.</small>
                        </td>
                    `;
                    tbody.appendChild(emptyMsgRow);
                }
            } else if (emptyMsgRow) {
                emptyMsgRow.remove();
            }
        });
    });

    // 2. SweetAlert2 Confirmation Interceptor for Forms and Action Buttons
    document.querySelectorAll('[data-confirm]').forEach(el => {
        el.addEventListener('click', function (e) {
            e.preventDefault();
            const message = this.getAttribute('data-confirm') || 'هل أنت متأكد من تنفيذ هذا الإجراء؟';
            const form = this.closest('form');
            const href = this.getAttribute('href');

            if (typeof Swal !== 'undefined') {
                Swal.fire({
                    title: 'تأكيد الإجراء',
                    text: message,
                    icon: 'warning',
                    showCancelButton: true,
                    confirmButtonColor: '#4f46e5',
                    cancelButtonColor: '#64748b',
                    confirmButtonText: 'نعم، تأكيد',
                    cancelButtonText: 'إلغاء',
                    reverseButtons: true
                }).then((result) => {
                    if (result.isConfirmed) {
                        if (form) {
                            form.submit();
                        } else if (href && href !== '#') {
                            window.location.href = href;
                        }
                    }
                });
            } else {
                if (confirm(message)) {
                    if (form) form.submit();
                    else if (href && href !== '#') window.location.href = href;
                }
            }
        });
    });
});

/**
 * Client-Side CSV Exporter for any HTML Table
 */
function exportTableToCSV(tableId, filename) {
    const table = document.getElementById(tableId);
    if (!table) return;

    let csv = [];
    const rows = table.querySelectorAll('tr');

    for (let i = 0; i < rows.length; i++) {
        // Skip hidden or non-printable rows
        if (rows[i].classList.contains('d-print-none') || rows[i].style.display === 'none') continue;

        let row = [], cols = rows[i].querySelectorAll('td, th');
        for (let j = 0; j < cols.length; j++) {
            // Skip action columns if marked
            if (cols[j].classList.contains('d-print-none') || cols[j].classList.contains('no-export')) continue;
            let data = cols[j].innerText.replace(/(\r\n|\n|\r)/gm, ' ').replace(/(\s\s+)/g, ' ').trim();
            data = data.replace(/"/g, '""');
            row.push('"' + data + '"');
        }
        if (row.length > 0) csv.push(row.join(','));
    }

    // Prepend UTF-8 BOM so Arabic displays correctly in Excel
    const csvContent = '\uFEFF' + csv.join('\n');
    const blob = new Blob([csvContent], { type: 'text/csv;charset=utf-8;' });
    const link = document.createElement('a');
    const url = URL.createObjectURL(blob);
    link.setAttribute('href', url);
    link.setAttribute('download', (filename || 'report') + '.csv');
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
}
