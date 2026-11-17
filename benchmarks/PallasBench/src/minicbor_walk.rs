//! Reader-only walk: logical tags/containers count; break markers do not.
use minicbor::{data::Type, decode::Error, Decoder};

pub fn count(bytes: &[u8]) -> Result<u64, Error> {
    let mut decoder = Decoder::new(bytes);
    let items = visit(&mut decoder)?;
    if decoder.position() != bytes.len() {
        return Err(Error::message("trailing CBOR bytes"));
    }
    Ok(items)
}

fn visit(d: &mut Decoder<'_>) -> Result<u64, Error> {
    let mut count = 1;
    match d.datatype()? {
        Type::U8 | Type::U16 | Type::U32 | Type::U64 => {
            d.u64()?;
        }
        Type::I8 | Type::I16 | Type::I32 | Type::I64 | Type::Int => {
            d.int()?;
        }
        Type::Bool => {
            d.bool()?;
        }
        Type::Null => {
            d.null()?;
        }
        Type::Bytes | Type::BytesIndef => {
            for chunk in d.bytes_iter()? {
                let _ = chunk?;
            }
        }
        Type::String | Type::StringIndef => {
            for chunk in d.str_iter()? {
                let _ = chunk?;
            }
        }
        Type::F16 => {
            d.skip()?;
        }
        Type::F32 => {
            d.f32()?;
        }
        Type::F64 => {
            d.f64()?;
        }
        Type::Tag => {
            d.tag()?;
            count += visit(d)?;
        }
        kind @ (Type::Array | Type::ArrayIndef | Type::Map | Type::MapIndef) => {
            let map = matches!(kind, Type::Map | Type::MapIndef);
            let size = if map { d.map()? } else { d.array()? };
            if let Some(size) = size {
                for _ in 0..size * if map { 2 } else { 1 } {
                    count += visit(d)?;
                }
            } else {
                let mut children = 0;
                while d.datatype()? != Type::Break {
                    count += visit(d)?;
                    children += 1;
                }
                d.skip()?;
                if map && children % 2 != 0 {
                    return Err(Error::message("odd indefinite map"));
                }
            }
        }
        _ => return Err(Error::message("unsupported CBOR item")),
    }
    Ok(count)
}

#[cfg(test)]
mod tests {
    use super::count;

    #[test]
    fn counts_nested_tags_containers_and_scalars() {
        // [tag(24, h'0102'), {_ h'': [true, null, -1] }]
        assert_eq!(
            count(&[0x82, 0xd8, 24, 0x42, 1, 2, 0xbf, 0x40, 0x83, 0xf5, 0xf6, 0x20, 0xff]).unwrap(),
            9
        );
        assert!(count(&[0x01, 0x01]).is_err());
        assert!(count(&[0xbf, 0x01, 0xff]).is_err());
        assert!(count(&[0x82, 0x01]).is_err());
    }
}
