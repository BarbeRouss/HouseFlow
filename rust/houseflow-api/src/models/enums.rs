//! Énumérations du domaine.
//!
//! Elles sont stockées en base dans des colonnes `varchar` (valeurs PascalCase, comme
//! la conversion `HasConversion<string>()` d'EF) — sauf `Periodicity`, stockée en
//! `integer` (ordinal de l'enum C#). En JSON elles se sérialisent en chaînes
//! PascalCase et se désérialisent sans tenir compte de la casse, comme le
//! `JsonStringEnumConverter` de .NET.

use std::fmt;
use std::str::FromStr;

use serde::{Deserialize, Deserializer, Serialize, Serializer};

/// Erreur de conversion d'une chaîne (base ou JSON) vers une énumération.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ParseEnumError {
    pub enum_name: &'static str,
    pub value: String,
}

impl fmt::Display for ParseEnumError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        write!(f, "'{}' is not a valid {}", self.value, self.enum_name)
    }
}

impl std::error::Error for ParseEnumError {}

/// Génère `Display`, `FromStr`, `TryFrom<String>`, `Serialize`/`Deserialize` pour une
/// énumération stockée en chaîne PascalCase.
macro_rules! string_enum {
    ($(#[$meta:meta])* $name:ident { $($variant:ident => $text:literal),+ $(,)? }) => {
        $(#[$meta])*
        #[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
        pub enum $name {
            $($variant),+
        }

        impl $name {
            pub fn as_str(self) -> &'static str {
                match self {
                    $(Self::$variant => $text),+
                }
            }
        }

        impl fmt::Display for $name {
            fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
                f.write_str(self.as_str())
            }
        }

        impl FromStr for $name {
            type Err = ParseEnumError;

            fn from_str(value: &str) -> Result<Self, Self::Err> {
                $(if value.eq_ignore_ascii_case($text) { return Ok(Self::$variant); })+
                Err(ParseEnumError { enum_name: stringify!($name), value: value.to_string() })
            }
        }

        impl TryFrom<String> for $name {
            type Error = ParseEnumError;

            fn try_from(value: String) -> Result<Self, Self::Error> {
                value.parse()
            }
        }

        impl Serialize for $name {
            fn serialize<S: Serializer>(&self, serializer: S) -> Result<S::Ok, S::Error> {
                serializer.serialize_str(self.as_str())
            }
        }

        impl<'de> Deserialize<'de> for $name {
            fn deserialize<D: Deserializer<'de>>(deserializer: D) -> Result<Self, D::Error> {
                let raw = String::deserialize(deserializer)?;
                raw.parse().map_err(serde::de::Error::custom)
            }
        }
    };
}

string_enum! {
    /// Rôle d'un membre dans une maison (colonne `"HouseMembers"."Role"`).
    HouseRole {
        Owner => "Owner",
        CollaboratorRW => "CollaboratorRW",
        CollaboratorRO => "CollaboratorRO",
        Tenant => "Tenant",
    }
}

string_enum! {
    /// État d'une invitation (colonne `"Invitations"."Status"`).
    InvitationStatus {
        Pending => "Pending",
        Accepted => "Accepted",
        Expired => "Expired",
        Revoked => "Revoked",
    }
}

string_enum! {
    /// Portée d'une clé API (colonne `"ApiKeys"."Scope"`).
    ApiKeyScope {
        ReadOnly => "ReadOnly",
        ReadWrite => "ReadWrite",
    }
}

/// Périodicité d'un type de maintenance.
///
/// Stockée en `integer` : l'ordinal doit rester celui de l'enum C#
/// (`Annual=0, Semestrial=1, Quarterly=2, Monthly=3, Custom=4`). Une valeur inconnue
/// est conservée telle quelle (`Unknown`) parce que le calculateur .NET la tolère et
/// retombe sur « + 1 an ».
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Periodicity {
    Annual,
    Semestrial,
    Quarterly,
    Monthly,
    Custom,
    Unknown(i32),
}

impl Periodicity {
    pub fn as_i32(self) -> i32 {
        match self {
            Periodicity::Annual => 0,
            Periodicity::Semestrial => 1,
            Periodicity::Quarterly => 2,
            Periodicity::Monthly => 3,
            Periodicity::Custom => 4,
            Periodicity::Unknown(value) => value,
        }
    }

    pub fn as_str(self) -> &'static str {
        match self {
            Periodicity::Annual => "Annual",
            Periodicity::Semestrial => "Semestrial",
            Periodicity::Quarterly => "Quarterly",
            Periodicity::Monthly => "Monthly",
            Periodicity::Custom => "Custom",
            Periodicity::Unknown(_) => "Unknown",
        }
    }
}

impl From<i32> for Periodicity {
    fn from(value: i32) -> Self {
        match value {
            0 => Periodicity::Annual,
            1 => Periodicity::Semestrial,
            2 => Periodicity::Quarterly,
            3 => Periodicity::Monthly,
            4 => Periodicity::Custom,
            other => Periodicity::Unknown(other),
        }
    }
}

impl fmt::Display for Periodicity {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str(self.as_str())
    }
}

impl FromStr for Periodicity {
    type Err = ParseEnumError;

    fn from_str(value: &str) -> Result<Self, Self::Err> {
        for candidate in [
            Periodicity::Annual,
            Periodicity::Semestrial,
            Periodicity::Quarterly,
            Periodicity::Monthly,
            Periodicity::Custom,
        ] {
            if value.eq_ignore_ascii_case(candidate.as_str()) {
                return Ok(candidate);
            }
        }
        // Le JSON .NET accepte aussi la forme numérique pour les enums.
        if let Ok(number) = value.parse::<i32>() {
            return Ok(Periodicity::from(number));
        }
        Err(ParseEnumError {
            enum_name: "Periodicity",
            value: value.to_string(),
        })
    }
}

impl Serialize for Periodicity {
    fn serialize<S: Serializer>(&self, serializer: S) -> Result<S::Ok, S::Error> {
        serializer.serialize_str(self.as_str())
    }
}

impl<'de> Deserialize<'de> for Periodicity {
    fn deserialize<D: Deserializer<'de>>(deserializer: D) -> Result<Self, D::Error> {
        struct Visitor;

        impl serde::de::Visitor<'_> for Visitor {
            type Value = Periodicity;

            fn expecting(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
                f.write_str("a periodicity name or its numeric value")
            }

            fn visit_str<E: serde::de::Error>(self, value: &str) -> Result<Self::Value, E> {
                value.parse().map_err(E::custom)
            }

            fn visit_u64<E: serde::de::Error>(self, value: u64) -> Result<Self::Value, E> {
                Ok(Periodicity::from(value as i32))
            }

            fn visit_i64<E: serde::de::Error>(self, value: i64) -> Result<Self::Value, E> {
                Ok(Periodicity::from(value as i32))
            }
        }

        deserializer.deserialize_any(Visitor)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn string_enums_round_trip_through_the_database_representation() {
        assert_eq!(HouseRole::Owner.to_string(), "Owner");
        assert_eq!("owner".parse::<HouseRole>().unwrap(), HouseRole::Owner);
        assert_eq!(
            "CollaboratorRW".parse::<HouseRole>().unwrap(),
            HouseRole::CollaboratorRW
        );
        assert!("Nope".parse::<HouseRole>().is_err());

        assert_eq!(
            "pending".parse::<InvitationStatus>().unwrap(),
            InvitationStatus::Pending
        );
        assert_eq!(
            "readonly".parse::<ApiKeyScope>().unwrap(),
            ApiKeyScope::ReadOnly
        );
    }

    #[test]
    fn enums_serialize_as_pascal_case_strings() {
        assert_eq!(
            serde_json::to_string(&ApiKeyScope::ReadWrite).unwrap(),
            "\"ReadWrite\""
        );
        // Désérialisation insensible à la casse, comme JsonStringEnumConverter.
        assert_eq!(
            serde_json::from_str::<ApiKeyScope>("\"readwrite\"").unwrap(),
            ApiKeyScope::ReadWrite
        );
    }

    #[test]
    fn periodicity_keeps_the_csharp_ordinals() {
        assert_eq!(Periodicity::Annual.as_i32(), 0);
        assert_eq!(Periodicity::Custom.as_i32(), 4);
        assert_eq!(Periodicity::from(999), Periodicity::Unknown(999));
        assert_eq!(Periodicity::Unknown(999).as_i32(), 999);
    }
}
