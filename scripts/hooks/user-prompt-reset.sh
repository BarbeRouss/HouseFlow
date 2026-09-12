#!/bin/bash
# UserPromptSubmit hook: un nouveau message de l'utilisateur remet à zéro la boucle de
# livraison (compteur d'itérations, marqueur "bloqué", rappel gh) de stop-ship-check.sh.
rm -f /tmp/houseflow-ship-blocked /tmp/houseflow-ship-iterations /tmp/houseflow-ship-gh-nudged
exit 0
