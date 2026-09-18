//! Hachage des mots de passe (BCrypt, coût 11 — défaut de BCrypt.Net).
//!
//! Les empreintes `$2a$` produites par .NET et `$2b$` produites ici sont
//! interchangeables : les deux backends vérifient les mots de passe de l'autre.

/// Coût utilisé par `BCrypt.Net.BCrypt.HashPassword` sans paramètre.
pub const COST: u32 = 11;

pub fn hash(password: &str) -> String {
    bcrypt::hash(password, COST).expect("bcrypt hashing cannot fail for a valid cost")
}

pub fn verify(password: &str, hash: &str) -> bool {
    bcrypt::verify(password, hash).unwrap_or(false)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn hashes_round_trip_and_use_cost_eleven() {
        let hashed = hash("Password123!");
        assert!(hashed.starts_with("$2b$11$") || hashed.starts_with("$2a$11$"));
        assert!(verify("Password123!", &hashed));
        assert!(!verify("WrongPassword!", &hashed));
    }

    #[test]
    fn dotnet_2a_hashes_are_accepted() {
        // BCrypt.Net écrit des empreintes "$2a$" ; elles doivent se vérifier ici.
        let dotnet_style = bcrypt::hash_with_result("admin", COST)
            .unwrap()
            .format_for_version(bcrypt::Version::TwoA);
        assert!(dotnet_style.starts_with("$2a$11$"));
        assert!(verify("admin", &dotnet_style));
        assert!(!verify("nope", &dotnet_style));
    }

    #[test]
    fn a_malformed_hash_never_verifies() {
        assert!(!verify("anything", "not-a-bcrypt-hash"));
    }
}
