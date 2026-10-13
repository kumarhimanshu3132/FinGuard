/* FinGuard Borrower Portal JavaScript Core */
document.addEventListener('DOMContentLoaded', function () {
    // 1. Multi-Step Application Wizard Logic
    const nextButtons = document.querySelectorAll('.bp-btn-next-step');
    const prevButtons = document.querySelectorAll('.bp-btn-prev-step');
    const steps = document.querySelectorAll('.bp-step');
    const panes = document.querySelectorAll('.bp-wizard-pane');

    function switchStep(targetIndex) {
        panes.forEach((pane, idx) => {
            if (idx === targetIndex) {
                pane.classList.remove('d-none');
            } else {
                pane.classList.add('d-none');
            }
        });

        steps.forEach((step, idx) => {
            if (idx === targetIndex) {
                step.classList.add('active');
                step.classList.remove('completed');
            } else if (idx < targetIndex) {
                step.classList.remove('active');
                step.classList.add('completed');
            } else {
                step.classList.remove('active', 'completed');
            }
        });

        window.scrollTo({ top: 0, behavior: 'smooth' });
    }

    nextButtons.forEach(btn => {
        btn.addEventListener('click', function () {
            const currentPane = this.closest('.bp-wizard-pane');
            const currentIndex = parseInt(currentPane.getAttribute('data-step-index') || 0, 10);
            
            // Validate required inputs in current pane
            const inputs = currentPane.querySelectorAll('input[required], select[required]');
            let isValid = true;
            inputs.forEach(input => {
                if (!input.checkValidity()) {
                    input.reportValidity();
                    isValid = false;
                }
            });

            if (isValid) {
                switchStep(currentIndex + 1);
            }
        });
    });

    prevButtons.forEach(btn => {
        btn.addEventListener('click', function () {
            const currentPane = this.closest('.bp-wizard-pane');
            const currentIndex = parseInt(currentPane.getAttribute('data-step-index') || 0, 10);
            if (currentIndex > 0) {
                switchStep(currentIndex - 1);
            }
        });
    });

    // 2. Dynamic Live Loan Application Calculator
    const principalInput = document.getElementById('bpReqPrincipal');
    const tenureSelect = document.getElementById('bpReqTenure');
    const displayPrincipal = document.getElementById('bpDispPrincipal');
    const displayEmi = document.getElementById('bpDispEmi');
    const displayTotal = document.getElementById('bpDispTotal');

    function updateAppPreview() {
        if (!principalInput || !tenureSelect) return;

        const p = parseFloat(principalInput.value) || 0;
        const n = parseInt(tenureSelect.value, 10) || 12;
        const annualRate = 12.0; // Standard 12% p.a.
        const r = (annualRate / 12.0) / 100.0;

        let emi = 0;
        if (r > 0 && n > 0 && p > 0) {
            emi = (p * r * Math.pow(1 + r, n)) / (Math.pow(1 + r, n) - 1);
        }

        if (displayPrincipal) {
            displayPrincipal.textContent = '₹' + p.toLocaleString('en-IN', { minimumFractionDigits: 2 });
        }
        if (displayEmi) {
            displayEmi.textContent = '₹' + emi.toLocaleString('en-IN', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
        }
        if (displayTotal) {
            displayTotal.textContent = '₹' + (emi * n).toLocaleString('en-IN', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
        }
        const reviewPrincipal = document.getElementById('reviewPrincipal');
        const reviewTenure = document.getElementById('reviewTenure');
        if (reviewPrincipal) {
            reviewPrincipal.textContent = '₹' + p.toLocaleString('en-IN', { minimumFractionDigits: 2 });
        }
        if (reviewTenure) {
            reviewTenure.textContent = n + ' Months';
        }
    }

    if (principalInput) principalInput.addEventListener('input', updateAppPreview);
    if (tenureSelect) tenureSelect.addEventListener('change', updateAppPreview);
    updateAppPreview();

    // 3. Loan Application Form Submission Handler
    const applyForm = document.getElementById('loanApplyForm');
    const agreeCheck = document.getElementById('agreeTermsCheck');
    const agreeError = document.getElementById('agreeTermsError');

    if (agreeCheck && agreeError) {
        agreeCheck.addEventListener('change', function () {
            if (this.checked) {
                agreeError.classList.add('d-none');
            }
        });
    }

    if (applyForm) {
        applyForm.addEventListener('submit', function (e) {
            if (agreeCheck && !agreeCheck.checked) {
                e.preventDefault();
                if (agreeError) {
                    agreeError.classList.remove('d-none');
                }
                return false;
            }
        });
    }

    // 4. Password Show/Hide Toggle
    const togglePassButtons = document.querySelectorAll('.bp-toggle-password');
    togglePassButtons.forEach(btn => {
        btn.addEventListener('click', function () {
            const targetId = this.getAttribute('data-target');
            const targetInput = document.getElementById(targetId);
            if (targetInput) {
                if (targetInput.type === 'password') {
                    targetInput.type = 'text';
                    this.innerHTML = '<i class="fa-solid fa-eye-slash"></i>';
                } else {
                    targetInput.type = 'password';
                    this.innerHTML = '<i class="fa-solid fa-eye"></i>';
                }
            }
        });
    });

    // 5. Responsive Mobile Sidebar Toggle
    const sidebar = document.getElementById('bpSidebar');
    const sidebarToggleBtn = document.getElementById('bpSidebarToggleBtn');
    const sidebarCloseBtn = document.getElementById('bpSidebarCloseBtn');
    const sidebarBackdrop = document.getElementById('bpSidebarBackdrop');

    if (sidebarToggleBtn && sidebar) {
        sidebarToggleBtn.addEventListener('click', function () {
            sidebar.classList.add('show');
            if (sidebarBackdrop) sidebarBackdrop.classList.add('show');
        });
    }

    function closeSidebar() {
        if (sidebar) sidebar.classList.remove('show');
        if (sidebarBackdrop) sidebarBackdrop.classList.remove('show');
    }

    if (sidebarCloseBtn) sidebarCloseBtn.addEventListener('click', closeSidebar);
    if (sidebarBackdrop) sidebarBackdrop.addEventListener('click', closeSidebar);
});
