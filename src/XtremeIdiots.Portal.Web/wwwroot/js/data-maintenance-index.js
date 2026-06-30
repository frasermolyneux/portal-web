document.addEventListener('DOMContentLoaded', function () {
    const confirmationInput = document.getElementById('deleteConfirmationText');
    const deleteButton = document.getElementById('deletePlayerButton');

    if (!confirmationInput || !deleteButton) {
        return;
    }

    const expectedPlayerId = (deleteButton.dataset.expectedPlayerId || '').trim().toLowerCase();

    function syncDeleteState() {
        const typedValue = (confirmationInput.value || '').trim().toLowerCase();
        deleteButton.disabled = typedValue.length === 0 || typedValue !== expectedPlayerId;
    }

    confirmationInput.addEventListener('input', syncDeleteState);
    syncDeleteState();
});
