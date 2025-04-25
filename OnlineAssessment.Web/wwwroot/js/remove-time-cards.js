// Script to remove time information cards from test cards
document.addEventListener('DOMContentLoaded', function() {
    // Function to remove time cards - will run immediately and then every 500ms
    function removeTimeCards() {
        // Target the specific blue cards with time information
        const timeCards = document.querySelectorAll('.test-card .card-body > div');

        timeCards.forEach(card => {
            // Check if this is a time information card
            if (card.textContent.includes('Current time (IST)') &&
                card.textContent.includes('Test start (IST)') &&
                card.textContent.includes('Test end (IST)')) {
                card.remove();
            }
        });
    }

    // Run immediately
    removeTimeCards();

    // Also run after a short delay to catch any dynamically added cards
    setTimeout(removeTimeCards, 500);
});
